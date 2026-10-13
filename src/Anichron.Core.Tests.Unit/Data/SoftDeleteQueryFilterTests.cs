using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data;

public sealed class SoftDeleteQueryFilterTests : IAsyncLifetime
{
    private readonly AnichronDbContext db = new(new DbContextOptionsBuilder<AnichronDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .EnableServiceProviderCaching(false)
        .Options);

    private readonly MediaAsset active = Asset("active.jpg", isSoftDeleted: false);
    private readonly MediaAsset deleted = Asset("deleted.jpg", isSoftDeleted: true);
    private readonly User user = new() { Id = Guid.NewGuid(), Username = "alice", Email = "a@example.com", PasswordHash = "h" };

    private static MediaAsset Asset(string name, bool isSoftDeleted)
        => new() { Id = Guid.NewGuid(), FilePath = name, FileName = name, ContentHash = name, IsSoftDeleted = isSoftDeleted };

    public async ValueTask InitializeAsync()
    {
        Seed();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
    }

    private void Seed()
    {
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/r", IsActive = true };
        active.StorageConfigId = deleted.StorageConfigId = config.Id;
        db.Users.Add(user);
        db.StorageConfigs.Add(config);
        db.MediaAssets.AddRange(active, deleted);
        foreach (var asset in new[] { active, deleted })
        {
            db.Metadata.Add(new Metadata { AssetId = asset.Id });
            db.ProxyFiles.Add(new ProxyFile { Id = Guid.NewGuid(), AssetId = asset.Id, ProxyPath = asset.FileName });
            db.Interactions.Add(new AssetInteraction { Id = Guid.NewGuid(), UserId = user.Id, AssetId = asset.Id });
            db.Bursts.Add(new Burst { Id = Guid.NewGuid(), PrimaryAssetId = asset.Id });
        }
    }

    public async ValueTask DisposeAsync() => await db.DisposeAsync();

    [Fact]
    public async Task MediaAssets_SoftDeletedAssetIsHiddenAsync()
        => (await db.MediaAssets.Select(a => a.Id).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Equal(active.Id);

    [Fact]
    public async Task Metadata_OfSoftDeletedAssetIsHiddenAsync()
        => (await db.Metadata.Select(m => m.AssetId).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Equal(active.Id);

    [Fact]
    public async Task ProxyFiles_OfSoftDeletedAssetAreHiddenAsync()
        => (await db.ProxyFiles.Select(p => p.AssetId).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Equal(active.Id);

    [Fact]
    public async Task Interactions_OnSoftDeletedAssetAreHiddenAsync()
        => (await db.Interactions.Select(i => i.AssetId).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Equal(active.Id);

    [Fact]
    public async Task Bursts_WhosePrimaryAssetIsSoftDeletedAreHiddenAsync()
        => (await db.Bursts.Select(b => b.PrimaryAssetId).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Equal(active.Id);

    [Fact]
    public async Task IgnoreQueryFilters_StillSeesSoftDeletedAssetsAsync()
        => (await db.MediaAssets.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(2);
}
