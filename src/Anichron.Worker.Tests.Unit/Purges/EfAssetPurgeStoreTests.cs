using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Purges;

namespace Anichron.Worker.Tests.Unit.Purges;

public sealed class EfAssetPurgeStoreTests : IAsyncDisposable
{
    private static readonly Guid ConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid OtherConfigId = Guid.Parse("c2000000-0000-0000-0000-000000000000");

    private readonly AnichronDbContext db = AnichronDbContexts.InMemory(freshModel: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EfAssetPurgeStore Store => new(db);

    public async ValueTask DisposeAsync() => await db.DisposeAsync();

    private async Task<MediaAsset> AssetAsync(Guid configId, bool softDeleted, string? proxyPath = null)
    {
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            StorageConfigId = configId,
            FilePath = "a.jpg",
            FileName = "a.jpg",
            ContentHash = "a",
            IsSoftDeleted = softDeleted,
        };
        db.MediaAssets.Add(asset);
        if (proxyPath is not null)
            db.ProxyFiles.Add(new ProxyFile { Id = Guid.NewGuid(), AssetId = asset.Id, ProxyPath = proxyPath });
        await db.SaveChangesAsync(Ct);
        return asset;
    }

    [Fact]
    public async Task CountPresentAsync_CountsTheConfigsRowsAmongTheIdsWhateverTheirStatusAsync()
    {
        var active = await AssetAsync(ConfigId, softDeleted: false);
        var softDeleted = await AssetAsync(ConfigId, softDeleted: true);
        var otherConfigs = await AssetAsync(OtherConfigId, softDeleted: false);
        await AssetAsync(ConfigId, softDeleted: false);

        var present = await Store.CountPresentAsync(ConfigId, [active.Id, softDeleted.Id, otherConfigs.Id, Guid.NewGuid()], Ct);

        present.Should().Be(2);
    }

    [Fact]
    public async Task IsReferencedAsync_PathOfASoftDeletedRow_IsReferencedAsync()
    {
        await AssetAsync(ConfigId, softDeleted: true, proxyPath: "ab/x/thumbnail.jpg");

        (await Store.IsReferencedAsync("ab/x/thumbnail.jpg", Ct), await Store.IsReferencedAsync("ab/y/thumbnail.jpg", Ct))
            .Should().Be((true, false));
    }
}
