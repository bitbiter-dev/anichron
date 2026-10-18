using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class MediaAssetRepositoryTests
{
    private static readonly Guid ConfigA = Guid.NewGuid();
    private static readonly Guid ConfigB = Guid.NewGuid();

    private static AnichronDbContext CreateDb(out string name)
    {
        name = Guid.NewGuid().ToString();
        return AnichronDbContexts.InMemory(name);
    }

    private static MediaAsset MakeAsset(Guid configId, string hash)
        => new() { Id = Guid.NewGuid(), StorageConfigId = configId, ContentHash = hash, FilePath = hash, FileName = hash };

    private static async Task<MediaAsset?> FindAsync(MediaAsset[] seeded, string hash, Guid configId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = AnichronDbContexts.InMemory();
        db.MediaAssets.AddRange(seeded);
        await db.SaveChangesAsync(ct);

        return await new EfMediaAssetRepository(db).FindByHashAsync(hash, configId, ct);
    }

    [Fact]
    public async Task FindByHashAsync_SameHashSameConfig_ReturnsTheAsset()
    {
        var asset = MakeAsset(ConfigA, "cafe");

        var result = await FindAsync([asset], "cafe", ConfigA);

        result.Should().NotBeNull();
        result.Id.Should().Be(asset.Id);
    }

    [Fact]
    public async Task FindByHashAsync_SameHashOtherConfig_ReturnsNull()
        => (await FindAsync([MakeAsset(ConfigB, "cafe")], "cafe", ConfigA)).Should().BeNull();

    [Fact]
    public async Task FindByHashAsync_OtherHashSameConfig_ReturnsNull()
        => (await FindAsync([MakeAsset(ConfigA, "beef")], "cafe", ConfigA)).Should().BeNull();

    [Fact]
    public async Task FindByHashAsync_ByteIdenticalFilesUnderTwoConfigs_ReturnsTheOneForTheRequestedConfig()
    {
        var inB = MakeAsset(ConfigB, "cafe");

        var result = await FindAsync([MakeAsset(ConfigA, "cafe"), inB], "cafe", ConfigB);

        result.Should().NotBeNull();
        result.Id.Should().Be(inB.Id);
    }

    [Fact]
    public async Task Add_TracksTheAssetForInsert()
    {
        await using var db = AnichronDbContexts.InMemory();
        var asset = MakeAsset(ConfigA, "cafe");

        new EfMediaAssetRepository(db).Add(asset);

        db.Entry(asset).State.Should().Be(EntityState.Added);
    }

    private static MediaAsset AssetAt(Guid configId, string hash, string filePath, bool softDeleted = false)
        => new()
        {
            Id = Guid.NewGuid(),
            StorageConfigId = configId,
            ContentHash = hash,
            FilePath = filePath,
            FileName = filePath,
            IsSoftDeleted = softDeleted,
        };

    private static async Task<IReadOnlyList<MediaAsset>> FindByHashesOrPathsAsync(
        MediaAsset[] seeded, string[] hashes, string[] paths)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var seed = CreateDb(out var name);
        seed.MediaAssets.AddRange(seeded);
        await seed.SaveChangesAsync(ct);
        await using var db = AnichronDbContexts.InMemory(name);

        return await new EfMediaAssetRepository(db).FindByHashesOrPathsAsync(ConfigA, hashes, paths, ct);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_ActiveRowOfAHash_ComesBackWithItsProxyRows()
    {
        var wanted = AssetAt(ConfigA, "cafe", "a.mov");
        wanted.ProxyFiles = [new ProxyFile { Id = Guid.NewGuid(), ProxyType = ProxyType.WebVideo, ProxyPath = "ca/x/video_720p.mp4" }];

        var result = await FindByHashesOrPathsAsync([wanted], ["beef", "cafe"], ["elsewhere.mov"]);

        result.Should().ContainSingle().Which.Id.Should().Be(wanted.Id);
        result[0].ProxyFiles.Should().ContainSingle().Which.ProxyType.Should().Be(ProxyType.WebVideo);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_SoftDeletedRowMatchedOnlyByHash_IsLeftOut()
        => (await FindByHashesOrPathsAsync([AssetAt(ConfigA, "cafe", "a.jpg", softDeleted: true)], ["cafe"], ["b.jpg"]))
            .Should().BeEmpty();

    [Fact]
    public async Task FindByHashesOrPathsAsync_RowAtAPath_ComesBackEvenWhenSoftDeleted()
    {
        var active = AssetAt(ConfigA, "beef", "a.jpg");
        var trashed = AssetAt(ConfigA, "f00d", "b.jpg", softDeleted: true);

        var result = await FindByHashesOrPathsAsync([active, trashed], ["cafe"], ["a.jpg", "b.jpg"]);

        result.Select(a => a.Id).Should().BeEquivalentTo([active.Id, trashed.Id]);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_RowsOfAnotherConfig_AreLeftOut()
        => (await FindByHashesOrPathsAsync([AssetAt(ConfigB, "cafe", "a.jpg")], ["cafe"], ["a.jpg"]))
            .Should().BeEmpty();

    [Fact]
    public async Task FindByHashesOrPathsAsync_RowMatchingNeitherHashNorPath_IsLeftOut()
        => (await FindByHashesOrPathsAsync([AssetAt(ConfigA, "beef", "b.jpg")], ["cafe"], ["a.jpg"]))
            .Should().BeEmpty();

    [Fact]
    public void AddProxyFile_TracksTheRowForInsertEvenWithItsIdPreset()
    {
        using var db = AnichronDbContexts.InMemory();
        var proxy = new ProxyFile { Id = Guid.NewGuid(), AssetId = Guid.NewGuid(), ProxyType = ProxyType.WebVideo };

        new EfMediaAssetRepository(db).AddProxyFile(proxy);

        db.Entry(proxy).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public void RemoveProxyFile_TracksTheRowForDelete()
    {
        using var db = AnichronDbContexts.InMemory();
        var proxy = new ProxyFile { Id = Guid.NewGuid(), AssetId = Guid.NewGuid(), ProxyType = ProxyType.WebVideo };
        db.ProxyFiles.Attach(proxy);

        new EfMediaAssetRepository(db).RemoveProxyFile(proxy);

        db.Entry(proxy).State.Should().Be(EntityState.Deleted);
    }

    [Fact]
    public async Task LoadMetadataAsync_LoadsTheAssetsMetadataRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = AssetAt(ConfigA, "cafe", "a.jpg");
        asset.Metadata = new Metadata { AssetId = asset.Id, Width = 640, Height = 480 };
        await using var seed = CreateDb(out var name);
        seed.MediaAssets.Add(asset);
        await seed.SaveChangesAsync(ct);
        await using var db = AnichronDbContexts.InMemory(name);
        var tracked = await db.MediaAssets.SingleAsync(a => a.Id == asset.Id, ct);

        await new EfMediaAssetRepository(db).LoadMetadataAsync(tracked, ct);

        tracked.Metadata.Should().NotBeNull();
        tracked.Metadata.Width.Should().Be(640);
    }
}
