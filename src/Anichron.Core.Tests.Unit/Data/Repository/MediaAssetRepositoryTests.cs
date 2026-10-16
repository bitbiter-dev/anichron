using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class MediaAssetRepositoryTests
{
    private static readonly Guid ConfigA = Guid.NewGuid();
    private static readonly Guid ConfigB = Guid.NewGuid();

    private static AnichronDbContext CreateDb() => CreateDb(out _);

    private static AnichronDbContext CreateDb(out string name)
    {
        name = Guid.NewGuid().ToString();
        return CreateDb(name);
    }

    private static AnichronDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AnichronDbContext(options);
    }

    private static MediaAsset MakeAsset(Guid configId, string hash)
        => new() { Id = Guid.NewGuid(), StorageConfigId = configId, ContentHash = hash, FilePath = hash, FileName = hash };

    private static async Task<MediaAsset?> FindAsync(MediaAsset[] seeded, string hash, Guid configId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
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
        await using var db = CreateDb();
        var asset = MakeAsset(ConfigA, "cafe");

        new EfMediaAssetRepository(db).Add(asset);

        db.Entry(asset).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task FindByHashWithProxyFilesAsync_ReturnsTheConfigsAssetWithItsProxyRowsLoaded()
    {
        var ct = TestContext.Current.CancellationToken;
        var wanted = MakeAsset(ConfigA, "cafe");
        wanted.ProxyFiles = [new ProxyFile { Id = Guid.NewGuid(), ProxyType = ProxyType.WebVideo, ProxyPath = "ca/x/video_720p.mp4" }];
        var otherConfig = MakeAsset(ConfigB, "cafe");
        var otherHash = MakeAsset(ConfigA, "beef");
        await using var seed = CreateDb(out var name);
        seed.MediaAssets.AddRange(wanted, otherConfig, otherHash);
        await seed.SaveChangesAsync(ct);
        await using var db = CreateDb(name);

        var result = await new EfMediaAssetRepository(db).FindByHashWithProxyFilesAsync("cafe", ConfigA, ct);

        result.Should().NotBeNull();
        result.Id.Should().Be(wanted.Id);
        result.ProxyFiles.Should().ContainSingle().Which.ProxyType.Should().Be(ProxyType.WebVideo);
    }

    [Theory]
    [InlineData("cafe", false)]
    [InlineData("beef", true)]
    public async Task FindByHashWithProxyFilesAsync_AssetMatchingOnlyHashOrOnlyConfig_ReturnsNull(string hash, bool sameConfig)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.MediaAssets.Add(MakeAsset(sameConfig ? ConfigA : ConfigB, hash));
        await db.SaveChangesAsync(ct);

        var result = await new EfMediaAssetRepository(db).FindByHashWithProxyFilesAsync("cafe", ConfigA, ct);

        result.Should().BeNull();
    }

    [Fact]
    public void AddProxyFile_TracksTheRowForInsertEvenWithItsIdPreset()
    {
        using var db = CreateDb();
        var proxy = new ProxyFile { Id = Guid.NewGuid(), AssetId = Guid.NewGuid(), ProxyType = ProxyType.WebVideo };

        new EfMediaAssetRepository(db).AddProxyFile(proxy);

        db.Entry(proxy).State.Should().Be(EntityState.Added);
    }
}
