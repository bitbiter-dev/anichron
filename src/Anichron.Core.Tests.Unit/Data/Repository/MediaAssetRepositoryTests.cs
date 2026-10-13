using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class MediaAssetRepositoryTests
{
    private static readonly Guid ConfigA = Guid.NewGuid();
    private static readonly Guid ConfigB = Guid.NewGuid();

    private static AnichronDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
}
