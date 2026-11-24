using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Tests.Integration.Data;

public sealed class MediaAssetRepositoryTests(PostgresServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Library(string ConnectionString, Guid Config, Guid OtherConfig, Guid User);

    private async Task<Library> LibraryAsync(params Func<Guid, Guid, MediaAsset>[] assets)
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        var (user, config, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = PostgresServer.Context(connectionString);
        db.Users.Add(new User { Id = user, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.AddRange(
            new UserStorageConfig { Id = config, UserId = user, RootPath = "/data/originals/mira", IsActive = true },
            new UserStorageConfig { Id = other, UserId = user, RootPath = "/data/originals/mira-phone", IsActive = true });
        db.MediaAssets.AddRange(assets.Select(asset => asset(config, other)));
        await db.SaveChangesAsync(Ct);
        return new Library(connectionString, config, other, user);
    }

    private static Func<Guid, Guid, MediaAsset> Photo(Guid id, string path, bool softDeleted = false, bool inOther = false)
        => (config, other) => new MediaAsset
        {
            Id = id,
            StorageConfigId = inOther ? other : config,
            FilePath = path,
            FileName = path,
            ContentHash = path,
            MediaType = MediaType.Image,
            IsSoftDeleted = softDeleted,
            SoftDeleteReason = softDeleted ? SoftDeleteReason.Overwritten : null,
        };

    private static async Task<Dictionary<Guid, SoftDeleteReason?>> ReasonByIdAsync(string connectionString)
    {
        await using var db = PostgresServer.Context(connectionString);
        return await db.MediaAssets.IgnoreQueryFilters().ToDictionaryAsync(a => a.Id, a => a.SoftDeleteReason, Ct);
    }

    [Fact]
    public async Task SoftDeleteAsync_SoftDeletesOnlyTheGivenActiveRowsAndCountsThemAsync()
    {
        var (gone, alsoGone, kept, alreadyTrashed) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var library = await LibraryAsync(
            Photo(gone, "a.jpg"), Photo(alsoGone, "b.jpg"), Photo(kept, "c.jpg"), Photo(alreadyTrashed, "d.jpg", softDeleted: true));
        await using var db = PostgresServer.Context(library.ConnectionString);

        var softDeleted = await new EfMediaAssetRepository(db).SoftDeleteAsync([gone, alsoGone, alreadyTrashed], SoftDeleteReason.Missing, Ct);

        softDeleted.Should().Be(2);
        (await ReasonByIdAsync(library.ConnectionString)).Should().BeEquivalentTo(new Dictionary<Guid, SoftDeleteReason?>
        {
            [gone] = SoftDeleteReason.Missing,
            [alsoGone] = SoftDeleteReason.Missing,
            [kept] = null,
            [alreadyTrashed] = SoftDeleteReason.Overwritten,
        });
    }

    [Fact]
    public async Task SoftDeleteAsync_MoreRowsThanOneBatch_SoftDeletesThemAllAsync()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var library = await LibraryAsync([.. ids.Select((id, index) => Photo(id, $"{index}.jpg"))]);
        await using var db = PostgresServer.Context(library.ConnectionString);

        var softDeleted = await new EfMediaAssetRepository(db).SoftDeleteAsync(ids, SoftDeleteReason.Missing, batchSize: 2, Ct);

        softDeleted.Should().Be(5);
        (await ReasonByIdAsync(library.ConnectionString)).Values.Should().AllSatisfy(value => value.Should().Be(SoftDeleteReason.Missing));
    }

    [Fact]
    public async Task SoftDeleteAsync_KeepsTheRowsInteractionsAsync()
    {
        var asset = Guid.NewGuid();
        var library = await LibraryAsync(Photo(asset, "a.jpg"));
        await using (var seed = PostgresServer.Context(library.ConnectionString))
        {
            seed.Interactions.Add(new AssetInteraction { Id = Guid.NewGuid(), AssetId = asset, UserId = library.User, IsStarred = true });
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = PostgresServer.Context(library.ConnectionString);
        await new EfMediaAssetRepository(db).SoftDeleteAsync([asset], SoftDeleteReason.Missing, Ct);

        (await db.Interactions.IgnoreQueryFilters().SingleAsync(Ct)).IsStarred.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, SoftDeleteReason.Missing)]
    public async Task SoftDeleteReason_IsSetExactlyOnSoftDeletedRowsAsync(bool softDeleted, SoftDeleteReason? reason)
    {
        var library = await LibraryAsync();
        await using var db = PostgresServer.Context(library.ConnectionString);
        var row = Photo(Guid.NewGuid(), "a.jpg")(library.Config, library.OtherConfig);
        (row.IsSoftDeleted, row.SoftDeleteReason) = (softDeleted, reason);
        db.MediaAssets.Add(row);

        var save = () => db.SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be(MediaAssetIndexNames.SoftDeleteReasonSetWhenSoftDeleted);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_UsesOnlyIndexScansAsync()
    {
        var library = await LibraryAsync();
        await using var db = PostgresServer.Context(library.ConnectionString);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "MediaAssets" ("Id", "StorageConfigId", "FilePath", "FileName", "ContentHash", "DateCaptured",
                "Month", "Day", "Year", "MediaType", "IsSoftDeleted", "SoftDeleteReason", "LastSeenOnNas")
            SELECT gen_random_uuid(), {library.Config}, n || '.jpg', n || '.jpg', 'hash' || n, '2024-01-01',
                1, 1, 2024, 'Image', n % 50 = 0, CASE WHEN n % 50 = 0 THEN 'Missing' END, now()
            FROM generate_series(1, 5000) AS n
            """, Ct);
        await db.Database.ExecuteSqlRawAsync("""ANALYZE "MediaAssets" """, Ct);
        await using var command = EfMediaAssetRepository
            .HashesOrPathsQuery(db, library.Config, ["hash1", "hash50"], ["7.jpg"])
            .CreateDbCommand();
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET enable_seqscan = off", Ct);
        command.CommandText = "EXPLAIN " + command.CommandText;

        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
                plan.Add(reader.GetString(0));
        }

        plan.Should().NotContain(line => line.Contains("Seq Scan"));
        string.Join('\n', plan).Should()
            .Contain("ix_mediaassets_storageconfigid_contenthash_active")
            .And.Contain("ix_mediaassets_storageconfigid_contenthash_softdeleted")
            .And.Contain("IX_MediaAssets_StorageConfigId_FilePath");
    }
}
