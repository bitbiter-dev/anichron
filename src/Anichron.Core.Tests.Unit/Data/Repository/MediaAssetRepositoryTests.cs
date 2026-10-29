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
    public async Task FindByHashAsync_OnlyASoftDeletedRowHoldsTheHash_ReturnsNull()
    {
        var trashed = MakeAsset(ConfigA, "cafe");
        trashed.IsSoftDeleted = true;

        (await FindAsync([trashed], "cafe", ConfigA)).Should().BeNull();
    }

    [Fact]
    public void FindByHash_StatesTheActiveRowInItsOwnPredicate()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfMediaAssetRepository.ActiveByHashQuery(db, "cafe", ConfigA).ToQueryString();

        sql.Should().Contain("""WHERE m."ContentHash" = @contentHash AND m."StorageConfigId" = @storageConfigId AND NOT (m."IsSoftDeleted")""");
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

    private static async Task<IReadOnlyList<MatchedAsset>> FindByHashesOrPathsAsync(
        MediaAsset[] seeded, string[] hashes, string[] paths)
    {
        await using var db = await SeededAsync(seeded);
        return await new EfMediaAssetRepository(db).FindByHashesOrPathsAsync(
            ConfigA, hashes, paths, TestContext.Current.CancellationToken);
    }

    private static async Task<AnichronDbContext> SeededAsync(MediaAsset[] seeded)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var seed = CreateDb(out var name);
        seed.MediaAssets.AddRange(seeded);
        await seed.SaveChangesAsync(ct);
        return AnichronDbContexts.InMemory(name);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_ActiveRowOfAHash_ComesBackWithItsProxyRows()
    {
        var wanted = AssetAt(ConfigA, "cafe", "a.mov");
        wanted.ProxyFiles = [new ProxyFile { Id = Guid.NewGuid(), ProxyType = ProxyType.WebVideo, ProxyPath = "ca/x/video_720p.mp4" }];

        var result = await FindByHashesOrPathsAsync([wanted], ["beef", "cafe"], ["elsewhere.mov"]);

        result.Should().ContainSingle().Which.Asset.Id.Should().Be(wanted.Id);
        result[0].Asset.ProxyFiles.Should().ContainSingle().Which.ProxyType.Should().Be(ProxyType.WebVideo);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_ReturnsTrackedRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync([AssetAt(ConfigA, "cafe", "a.jpg")]);

        var result = await new EfMediaAssetRepository(db).FindByHashesOrPathsAsync(ConfigA, ["cafe"], [], ct);

        db.Entry(result.Should().ContainSingle().Which.Asset).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_RowNoPhotoPointsAt_IsNotALivePhotoVideo()
        => (await FindByHashesOrPathsAsync([AssetAt(ConfigA, "cafe", "a.mov")], ["cafe"], []))
            .Should().ContainSingle().Which.IsLivePhotoVideo.Should().BeFalse();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindByHashesOrPathsAsync_RowAPhotoPointsAt_IsALivePhotoVideoEvenWhenThePhotoIsSoftDeleted(bool photoSoftDeleted)
    {
        var video = AssetAt(ConfigA, "cafe", "a.mov");
        var photo = AssetAt(ConfigA, "beef", "a.heic", softDeleted: photoSoftDeleted);
        photo.PairedAssetId = video.Id;

        var result = await FindByHashesOrPathsAsync([video, photo], ["cafe"], []);

        result.Should().ContainSingle().Which.IsLivePhotoVideo.Should().BeTrue();
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_PhotoPointingAtAnotherRow_LeavesThisRowNotALivePhotoVideo()
    {
        var (video, other) = (AssetAt(ConfigA, "cafe", "a.mov"), AssetAt(ConfigA, "f00d", "b.mov"));
        var photo = AssetAt(ConfigA, "beef", "b.heic");
        photo.PairedAssetId = other.Id;

        var result = await FindByHashesOrPathsAsync([video, other, photo], ["cafe"], []);

        result.Should().ContainSingle().Which.IsLivePhotoVideo.Should().BeFalse();
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_SoftDeletedRowMatchedOnlyByHash_ComesBack()
    {
        var trashed = AssetAt(ConfigA, "cafe", "a.jpg", softDeleted: true);

        var result = await FindByHashesOrPathsAsync([trashed], ["cafe"], ["b.jpg"]);

        result.Should().ContainSingle().Which.Asset.Id.Should().Be(trashed.Id);
    }

    [Fact]
    public void FindByHashesOrPaths_TranslatesToOneBranchPerIndex()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfMediaAssetRepository.HashesOrPathsQuery(db, ConfigA, ["cafe"], ["a.jpg"]).ToQueryString();

        sql.Should().Contain("""
            WHERE m."StorageConfigId" = @storageConfigId AND ((NOT (m."IsSoftDeleted") AND m."ContentHash" = ANY (@contentHashes)) OR (m."IsSoftDeleted" AND m."ContentHash" = ANY (@contentHashes)) OR m."FilePath" = ANY (@filePaths))
            """);
    }

    [Fact]
    public void FindByHashesOrPaths_CountsEveryPhotoPointingAtARowSoftDeletedOrNot()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfMediaAssetRepository.HashesOrPathsQuery(db, ConfigA, ["cafe"], ["a.jpg"]).ToQueryString();

        sql.Should().Contain("""
            EXISTS (
                SELECT 1
                FROM "MediaAssets" AS m0
                WHERE m0."PairedAssetId" = m."Id")
            """);
    }

    [Fact]
    public async Task FindByHashesOrPathsAsync_RowAtAPath_ComesBackEvenWhenSoftDeleted()
    {
        var active = AssetAt(ConfigA, "beef", "a.jpg");
        var trashed = AssetAt(ConfigA, "f00d", "b.jpg", softDeleted: true);

        var result = await FindByHashesOrPathsAsync([active, trashed], ["cafe"], ["a.jpg", "b.jpg"]);

        result.Select(row => row.Asset.Id).Should().BeEquivalentTo([active.Id, trashed.Id]);
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
    public void AddProxyFiles_TracksEveryRowForInsertEvenWithItsIdPreset()
    {
        using var db = AnichronDbContexts.InMemory();
        var assetId = Guid.NewGuid();
        ProxyFile[] proxies =
        [
            new() { Id = Guid.NewGuid(), AssetId = assetId, ProxyType = ProxyType.Thumbnail },
            new() { Id = Guid.NewGuid(), AssetId = assetId, ProxyType = ProxyType.FullPreview },
        ];

        new EfMediaAssetRepository(db).AddProxyFiles(proxies);

        proxies.Select(proxy => db.Entry(proxy).State).Should().Equal(EntityState.Added, EntityState.Added);
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

    [Fact]
    public async Task LoadMetadataAsync_SoftDeletedAsset_StillLoadsItsMetadataRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = AssetAt(ConfigA, "cafe", "a.jpg", softDeleted: true);
        asset.Metadata = new Metadata { AssetId = asset.Id, Width = 640 };
        await using var seed = CreateDb(out var name);
        seed.MediaAssets.Add(asset);
        await seed.SaveChangesAsync(ct);
        await using var db = AnichronDbContexts.InMemory(name);
        var tracked = await db.MediaAssets.IgnoreQueryFilters().SingleAsync(a => a.Id == asset.Id, ct);

        await new EfMediaAssetRepository(db).LoadMetadataAsync(tracked, ct);

        tracked.Metadata.Should().NotBeNull();
        tracked.Metadata.Width.Should().Be(640);
    }

    // ==========================================================================
    // GetActivePathsAsync
    // ==========================================================================

    [Fact]
    public async Task GetActivePathsAsync_ReturnsTheIdAndPathOfEachActiveRowOfTheConfig()
    {
        var ct = TestContext.Current.CancellationToken;
        var (first, second) = (AssetAt(ConfigA, "cafe", "2023/a.jpg"), AssetAt(ConfigA, "beef", "2024/b.jpg"));
        await using var seed = CreateDb(out var name);
        seed.MediaAssets.AddRange(
            first, second, AssetAt(ConfigA, "f00d", "trashed.jpg", softDeleted: true), AssetAt(ConfigB, "d00d", "other.jpg"));
        await seed.SaveChangesAsync(ct);
        await using var db = AnichronDbContexts.InMemory(name);

        var result = await new EfMediaAssetRepository(db).GetActivePathsAsync(ConfigA, ct);

        result.Should().BeEquivalentTo([new AssetPath(first.Id, "2023/a.jpg"), new AssetPath(second.Id, "2024/b.jpg")]);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetActivePathsAsync_SaysWhichPhotoPointsAtWhichVideo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (video, photo) = (AssetAt(ConfigA, "cafe", "a.mov"), AssetAt(ConfigA, "beef", "a.heic"));
        photo.PairedAssetId = video.Id;
        await using var db = await SeededAsync([video, photo]);

        var result = await new EfMediaAssetRepository(db).GetActivePathsAsync(ConfigA, ct);

        result.Should().BeEquivalentTo([new AssetPath(video.Id, "a.mov"), new AssetPath(photo.Id, "a.heic", PairedAssetId: video.Id)]);
    }

    [Fact]
    public void GetActivePaths_StatesTheActiveRowsInItsOwnPredicate()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfMediaAssetRepository.ActivePathsQuery(db, ConfigA).ToQueryString();

        sql.Should().Contain("""WHERE m."StorageConfigId" = @storageConfigId AND NOT (m."IsSoftDeleted")""");
    }

    // ==========================================================================
    // FindAsync
    // ==========================================================================

    [Fact]
    public async Task FindAsync_ActiveStoredRow_IsLoadedAndTracked()
    {
        var ct = TestContext.Current.CancellationToken;
        var video = AssetAt(ConfigA, "cafe", "a.mov");
        await using var db = await SeededAsync([video]);

        var found = await new EfMediaAssetRepository(db).FindAsync(video.Id, ct);

        found.Should().NotBeNull();
        found.FilePath.Should().Be("a.mov");
        db.Entry(found).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task FindAsync_SoftDeletedStoredRow_IsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var video = AssetAt(ConfigA, "cafe", "a.mov", softDeleted: true);
        await using var db = await SeededAsync([video]);

        (await new EfMediaAssetRepository(db).FindAsync(video.Id, ct)).Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_RowAddedButNotSaved_IsThatInstance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = AnichronDbContexts.InMemory();
        var video = AssetAt(ConfigA, "cafe", "a.mov");
        db.MediaAssets.Add(video);

        (await new EfMediaAssetRepository(db).FindAsync(video.Id, ct)).Should().BeSameAs(video);
    }

    // ==========================================================================
    // FindPhotosOfAsync
    // ==========================================================================

    [Fact]
    public async Task FindPhotosOfAsync_ReturnsTheActiveAndSoftDeletedRowsPointingAtTheVideoTracked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (video, other) = (AssetAt(ConfigA, "cafe", "a.mov"), AssetAt(ConfigA, "f00d", "b.mov"));
        var (photo, trashed, unrelated) = (
            AssetAt(ConfigA, "beef", "a.heic"), AssetAt(ConfigA, "d00d", "c.heic", softDeleted: true), AssetAt(ConfigA, "babe", "b.heic"));
        (photo.PairedAssetId, trashed.PairedAssetId, unrelated.PairedAssetId) = (video.Id, video.Id, other.Id);
        await using var db = await SeededAsync([video, other, photo, trashed, unrelated]);

        var found = await new EfMediaAssetRepository(db).FindPhotosOfAsync(video.Id, ct);

        found.Select(row => row.Id).Should().BeEquivalentTo([photo.Id, trashed.Id]);
        found.Should().AllSatisfy(row => db.Entry(row).State.Should().Be(EntityState.Unchanged));
    }

    [Fact]
    public async Task FindPhotosOfAsync_RowAlreadyTracked_ComesBackAsThatInstanceWithItsCurrentPointer()
    {
        var ct = TestContext.Current.CancellationToken;
        var video = AssetAt(ConfigA, "cafe", "a.mov");
        var photo = AssetAt(ConfigA, "beef", "a.heic");
        photo.PairedAssetId = video.Id;
        await using var db = await SeededAsync([video, photo]);
        var tracked = await db.MediaAssets.SingleAsync(row => row.Id == photo.Id, ct);
        tracked.PairedAssetId = null;

        var found = await new EfMediaAssetRepository(db).FindPhotosOfAsync(video.Id, ct);

        found.Should().ContainSingle().Which.Should().BeSameAs(tracked);
        tracked.PairedAssetId.Should().BeNull();
    }

    // ==========================================================================
    // SoftDeleteAsync
    // ==========================================================================

    [Fact]
    public void SoftDelete_StatesTheStillActiveRowsInItsOwnPredicate()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfMediaAssetRepository.StillActiveQuery(db, [Guid.NewGuid()]).ToQueryString();

        sql.Should().Contain("""WHERE m."Id" = ANY (@ids) AND NOT (m."IsSoftDeleted")""");
    }

    // ==========================================================================
    // FindSharedContentAsync
    // ==========================================================================

    private static readonly Guid Mira = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static UserStorageConfig ConfigOf(Guid userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, RootPath = $"/data/originals/{Guid.NewGuid():N}", IsActive = true };

    private static async Task<IReadOnlyList<ContentLocation>> FindSharedContentAsync(
        UserStorageConfig[] configs, MediaAsset[] assets, IReadOnlyCollection<Guid> crawled)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var seed = CreateDb(out var name);
        seed.StorageConfigs.AddRange(configs);
        seed.MediaAssets.AddRange(assets);
        await seed.SaveChangesAsync(ct);
        await using var db = AnichronDbContexts.InMemory(name);

        return await new EfMediaAssetRepository(db).FindSharedContentAsync(crawled, ct);
    }

    [Fact]
    public async Task FindSharedContentAsync_SameHashInTwoConfigsOfOneUser_ReturnsBothLocations()
    {
        var (home, phone) = (ConfigOf(Mira), ConfigOf(Mira));

        var result = await FindSharedContentAsync(
            [home, phone],
            [AssetAt(home.Id, "cafe", "2023/a.jpg"), AssetAt(phone.Id, "cafe", "dcim/a.jpg"), AssetAt(home.Id, "beef", "b.jpg")],
            [home.Id, phone.Id]);

        result.Should().BeEquivalentTo(
        [
            new ContentLocation(Mira, "cafe", home.Id, "2023/a.jpg"),
            new ContentLocation(Mira, "cafe", phone.Id, "dcim/a.jpg"),
        ]);
    }

    [Fact]
    public async Task FindSharedContentAsync_SameHashUnderTwoUsers_IsLeftOut()
    {
        var (mine, theirs) = (ConfigOf(Mira), ConfigOf(Alice));

        var result = await FindSharedContentAsync(
            [mine, theirs],
            [AssetAt(mine.Id, "cafe", "a.jpg"), AssetAt(theirs.Id, "cafe", "a.jpg")],
            [mine.Id, theirs.Id]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSharedContentAsync_ConfigNotCrawled_IsLeftOut()
    {
        var (home, phone) = (ConfigOf(Mira), ConfigOf(Mira));

        var result = await FindSharedContentAsync(
            [home, phone],
            [AssetAt(home.Id, "cafe", "a.jpg"), AssetAt(phone.Id, "cafe", "a.jpg")],
            [home.Id]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSharedContentAsync_SoftDeletedCopy_IsLeftOut()
    {
        var (home, phone) = (ConfigOf(Mira), ConfigOf(Mira));

        var result = await FindSharedContentAsync(
            [home, phone],
            [AssetAt(home.Id, "cafe", "a.jpg"), AssetAt(phone.Id, "cafe", "a.jpg", softDeleted: true)],
            [home.Id, phone.Id]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindSharedContent_TranslatesToOneStatementScopedToTheCrawledConfigs()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfMediaAssetRepository.SharedContentQuery(db, [Guid.NewGuid()]).ToQueryString();

        sql.Should().Contain("GROUP BY")
            .And.Contain("HAVING count(*)::int > 1")
            .And.Contain("""WHERE m."StorageConfigId" = ANY (@storageConfigIds) AND NOT (m."IsSoftDeleted") AND EXISTS""")
            .And.Contain("""WHERE m0."StorageConfigId" = ANY (@storageConfigIds) AND NOT (m0."IsSoftDeleted")""");
    }
}
