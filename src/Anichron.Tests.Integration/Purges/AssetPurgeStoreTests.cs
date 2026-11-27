using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Purges;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.IO.Abstractions;

namespace Anichron.Tests.Integration.Purges;

public sealed class AssetPurgeStoreTests(PostgresServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Config = Guid.NewGuid();
    private static readonly Guid OtherConfig = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private sealed record Library(string ConnectionString);

    private Task<Library> LibraryAsync(params MediaAsset[] assets)
        => LibraryAsync(db => db.MediaAssets.AddRange(assets));

    private async Task<Library> LibraryAsync(Action<AnichronDbContext> seed)
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        await using var db = PostgresServer.Context(connectionString);
        db.Users.Add(new User { Id = User, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.AddRange(
            new UserStorageConfig { Id = Config, UserId = User, RootPath = "/data/originals/mira", IsActive = true },
            new UserStorageConfig { Id = OtherConfig, UserId = User, RootPath = "/data/originals/mira-phone", IsActive = true });
        seed(db);
        await db.SaveChangesAsync(Ct);
        return new Library(connectionString);
    }

    private static MediaAsset Asset(Guid config, string name, bool softDeleted = true, params ProxyType[] proxies)
    {
        var id = Guid.NewGuid();
        return new MediaAsset
        {
            Id = id,
            StorageConfigId = config,
            FilePath = name,
            FileName = name,
            ContentHash = name,
            MediaType = MediaType.Image,
            IsSoftDeleted = softDeleted,
            SoftDeleteReason = softDeleted ? SoftDeleteReason.Missing : null,
            ProxyFiles = [.. proxies.Select(type => new ProxyFile { Id = Guid.NewGuid(), AssetId = id, ProxyType = type, ProxyPath = $"{name}/{type}" })],
        };
    }

    private static async Task<IReadOnlyList<PurgedProxy>> DeleteAsync(Library library, params Guid[] ids)
    {
        await using var db = PostgresServer.Context(library.ConnectionString);
        return await new EfAssetPurgeStore(db).DeleteSoftDeletedAsync(Config, ids, Ct);
    }

    private static async Task<List<Guid>> AssetIdsAsync(Library library)
    {
        await using var db = PostgresServer.Context(library.ConnectionString);
        return await db.MediaAssets.IgnoreQueryFilters().Select(a => a.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task DeleteSoftDeletedAsync_ReturnsThePathsTheCascadeRemovesAsync()
    {
        var gone = Asset(Config, "a", true, ProxyType.Thumbnail, ProxyType.FullPreview);
        var library = await LibraryAsync(gone);

        var purged = await DeleteAsync(library, gone.Id);

        purged.Should().BeEquivalentTo([new PurgedProxy(gone.Id, "a/Thumbnail"), new PurgedProxy(gone.Id, "a/FullPreview")]);
        (await AssetIdsAsync(library)).Should().BeEmpty();
        await using var db = PostgresServer.Context(library.ConnectionString);
        (await db.ProxyFiles.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteSoftDeletedAsync_RowWithoutProxies_IsReturnedWithoutAPathAsync()
    {
        var gone = Asset(Config, "a");
        var library = await LibraryAsync(gone);

        var purged = await DeleteAsync(library, gone.Id);

        purged.Should().Equal(new PurgedProxy(gone.Id, null));
    }

    [Fact]
    public async Task DeleteSoftDeletedAsync_RestoredRow_KeepsItsRowAndItsPathsAsync()
    {
        var restored = Asset(Config, "a", softDeleted: false, ProxyType.Thumbnail);
        var library = await LibraryAsync(restored);

        var purged = await DeleteAsync(library, restored.Id);

        purged.Should().BeEmpty();
        (await AssetIdsAsync(library)).Should().Equal(restored.Id);
        await using var db = PostgresServer.Context(library.ConnectionString);
        (await db.ProxyFiles.Select(p => p.ProxyPath).ToListAsync(Ct)).Should().Equal("a/Thumbnail");
    }

    [Fact]
    public async Task DeleteSoftDeletedAsync_AnotherConfigsId_IsUntouchedAsync()
    {
        var other = Asset(OtherConfig, "a", proxies: ProxyType.Thumbnail);
        var library = await LibraryAsync(other);

        var purged = await DeleteAsync(library, other.Id);

        purged.Should().BeEmpty();
        (await AssetIdsAsync(library)).Should().Equal(other.Id);
    }

    [Fact]
    public async Task DeleteSoftDeletedAsync_CascadesToTheRowsDataAndUnpairsItsPhotoAsync()
    {
        var at = Instant.FromUtc(2026, 11, 25, 9, 0);
        var video = Asset(Config, "v");
        var photo = Asset(Config, "p", softDeleted: false);
        photo.PairedAssetId = video.Id;
        var library = await LibraryAsync(db =>
        {
            db.MediaAssets.AddRange(video, photo);
            db.Metadata.Add(new Metadata { AssetId = video.Id });
            db.Interactions.Add(new AssetInteraction { Id = Guid.NewGuid(), UserId = User, AssetId = video.Id, IsStarred = true });
            db.BurstChecks.Add(new BurstCheck { AssetId = video.Id, CheckedAt = at });
        });

        await DeleteAsync(library, video.Id);

        await using var db = PostgresServer.Context(library.ConnectionString);
        (await db.Metadata.IgnoreQueryFilters().CountAsync(Ct), await db.Interactions.IgnoreQueryFilters().CountAsync(Ct), await db.BurstChecks.IgnoreQueryFilters().CountAsync(Ct))
            .Should().Be((0, 0, 0));
        (await db.MediaAssets.SingleAsync(a => a.Id == photo.Id, Ct)).PairedAssetId.Should().BeNull();
    }

    [Fact]
    public async Task CountPresentAsync_CountsOnlyTheConfigsRowsAmongTheIdsAsync()
    {
        var kept = Asset(Config, "a", softDeleted: false);
        var softDeleted = Asset(Config, "b");
        var other = Asset(OtherConfig, "c", softDeleted: false);
        var library = await LibraryAsync(kept, softDeleted, other, Asset(Config, "d", softDeleted: false));
        await using var db = PostgresServer.Context(library.ConnectionString);

        var present = await new EfAssetPurgeStore(db).CountPresentAsync(Config, [kept.Id, softDeleted.Id, other.Id, Guid.NewGuid()], Ct);

        present.Should().Be(2);
    }

    [Fact]
    public async Task IsReferencedAsync_SeesSoftDeletedRowsPathsAsync()
    {
        var library = await LibraryAsync(Asset(Config, "a", proxies: ProxyType.Thumbnail));
        await using var db = PostgresServer.Context(library.ConnectionString);
        var store = new EfAssetPurgeStore(db);

        (await store.IsReferencedAsync("a/Thumbnail", Ct), await store.IsReferencedAsync("b/Thumbnail", Ct)).Should().Be((true, false));
    }

    private static async Task<FileOutcome> ClassifyAsync(Library library, MediaAsset asset)
    {
        await using var db = PostgresServer.Context(library.ConnectionString);
        var context = new IngestionContext
        {
            Item = new SingleFileItem($"/data/originals/mira/{asset.FilePath}", asset.FilePath, MediaType.Image),
            Config = new UserStorageConfig { Id = Config, RootPath = "/data/originals/mira" },
            AttemptId = Guid.NewGuid(),
            ContentHash = asset.ContentHash,
            SecondaryHash = string.Empty,
        };
        var classification = await new IngestionClassifier(new EfMediaAssetRepository(db), Substitute.For<IFileSystem>()).ClassifyAsync(context, Ct);
        return classification.Primary.Outcome;
    }

    [Fact]
    public async Task DeleteSoftDeletedAsync_PurgedFileThatReappears_IsIngestedAsNewAsync()
    {
        var gone = Asset(Config, "2024/a.jpg");
        var library = await LibraryAsync(gone);
        (await ClassifyAsync(library, gone)).Should().Be(FileOutcome.AutoRestored, "before the purge the row would come back");

        await DeleteAsync(library, gone.Id);

        (await ClassifyAsync(library, gone)).Should().Be(FileOutcome.New);
    }
}
