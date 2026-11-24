using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Testing.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class SoftDeletedAssetReaderTests
{
    private static readonly Instant Monday = Instant.FromUtc(2026, 11, 16, 9, 0);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private sealed class Library
    {
        private readonly List<UserStorageConfig> configs = [];
        private readonly List<MediaAsset> assets = [];
        private readonly List<ProxyFile> proxies = [];
        private readonly List<Metadata> metadata = [];
        private readonly List<AssetInteraction> interactions = [];

        public Library()
        {
            MiraConfig = Config(Mira, "/data/originals/mira", isActive: true);
            AliceConfig = Config(Alice, "/data/originals/alice", isActive: true);
            MiraArchive = Config(Mira, "/data/originals/mira-archive", isActive: false);
        }

        public User Mira { get; } = new() { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com" };
        public User Alice { get; } = new() { Id = Guid.NewGuid(), Username = "alice", Email = "alice@example.com" };
        public UserStorageConfig MiraConfig { get; }
        public UserStorageConfig AliceConfig { get; }
        public UserStorageConfig MiraArchive { get; }

        public MediaAsset Asset(
            UserStorageConfig? config = null,
            SoftDeleteReason? reason = SoftDeleteReason.Missing,
            MediaType type = MediaType.Image,
            Instant? lastSeen = null,
            ProxyType[]? proxyTypes = null)
        {
            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                StorageConfigId = (config ?? MiraConfig).Id,
                FilePath = $"2024/{Guid.NewGuid():N}.jpg",
                FileName = "x.jpg",
                ContentHash = Guid.NewGuid().ToString("N"),
                Captured = Recorded.At(new LocalDateTime(2019, 7, 14, 9, 30)),
                MediaType = type,
                IsSoftDeleted = reason is not null,
                SoftDeleteReason = reason,
                LastSeenOnNas = lastSeen ?? Monday,
            };
            assets.Add(asset);
            proxies.AddRange((proxyTypes ?? []).Select(t => new ProxyFile
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                ProxyType = t,
                ProxyPath = $"{asset.ContentHash}/{t}",
            }));
            return asset;
        }

        public MediaAsset LivePhoto(MediaAsset video, SoftDeleteReason? reason = SoftDeleteReason.Missing)
        {
            var photo = Asset(reason: reason, type: MediaType.LivePhoto);
            photo.PairedAssetId = video.Id;
            return photo;
        }

        public void Describe(MediaAsset asset, MediaDescription description, string blurhash)
            => metadata.Add(new Metadata { AssetId = asset.Id, Description = description, Blurhash = blurhash });

        public void Hide(MediaAsset asset)
            => interactions.Add(new AssetInteraction { Id = Guid.NewGuid(), UserId = Mira.Id, AssetId = asset.Id, HiddenAt = Monday });

        public async Task<DbContextOptions<AnichronDbContext>> SeedAsync()
        {
            var options = AnichronDbContexts.InMemoryOptions();
            await using var db = new AnichronDbContext(options);
            db.Users.AddRange(Mira, Alice);
            db.StorageConfigs.AddRange(configs);
            db.MediaAssets.AddRange(assets);
            db.ProxyFiles.AddRange(proxies);
            db.Metadata.AddRange(metadata);
            db.Interactions.AddRange(interactions);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return options;
        }

        private UserStorageConfig Config(User owner, string rootPath, bool isActive)
        {
            var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = owner.Id, RootPath = rootPath, IsActive = isActive };
            configs.Add(config);
            return config;
        }
    }

    private static async Task<SoftDeletedPage> ReadPageAsync(Library library, AssetCursor? after = null, int size = 10)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        return await new EfSoftDeletedAssetReader(db).ReadPageAsync(library.Mira.Id, after, size, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadPageAsync_ListsOnlyTheCallersSoftDeletedAssetsInActiveConfigs()
    {
        var library = new Library();
        var gone = library.Asset();
        library.Asset(reason: null);
        library.Asset(library.AliceConfig);
        library.Asset(library.MiraArchive);

        var page = await ReadPageAsync(library);

        page.Items.Select(view => view.Id).Should().Equal(gone.Id);
    }

    [Fact]
    public async Task ReadPageAsync_ReportsThePathWhenItWasLastSeenAndWhyItWent()
    {
        var library = new Library();
        var gone = library.Asset(reason: SoftDeleteReason.Overwritten, lastSeen: Monday - Duration.FromDays(3));

        var page = await ReadPageAsync(library);

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            gone.FilePath,
            LastSeenOnNas = Instant.FromUtc(2026, 11, 13, 9, 0),
            Reason = SoftDeleteReason.Overwritten,
        });
    }

    [Fact]
    public async Task ReadPageAsync_MapsTheAssetsMediaFieldsAndProxies()
    {
        var library = new Library();
        var gone = library.Asset(type: MediaType.Video, proxyTypes: [ProxyType.Thumbnail, ProxyType.CompactPreview, ProxyType.WebVideo]);
        library.Describe(gone, MediaDescription.Unknown with { Width = 1920, Height = 1080, Orientation = 6, DurationInSeconds = 12 }, "LKO2?U");

        var page = await ReadPageAsync(library);

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            gone.ContentHash,
            MediaType = MediaType.Video,
            Year = 2019,
            Month = 7,
            Day = 14,
            Width = 1920,
            Height = 1080,
            Orientation = 6,
            DurationInSeconds = 12,
            Blurhash = "LKO2?U",
            HasThumbnail = true,
            HasPreview = false,
            HasCompactPreview = true,
            HasVideo = true,
            PairedMovId = (Guid?)null,
        });
    }

    [Fact]
    public async Task ReadPageAsync_UndatedAsset_IsListedWithoutADate()
    {
        var library = new Library();
        library.Asset().Captured = null;

        var page = await ReadPageAsync(library);

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Year = (int?)null, Month = (int?)null, Day = (int?)null });
    }

    [Fact]
    public async Task ReadPageAsync_HiddenAsset_IsListed()
    {
        var library = new Library();
        var gone = library.Asset();
        library.Hide(gone);

        var page = await ReadPageAsync(library);

        page.Items.Select(view => view.Id).Should().Equal(gone.Id);
    }

    [Fact]
    public async Task ReadPageAsync_SoftDeletedLivePhoto_IsOneItemWithItsVideo()
    {
        var library = new Library();
        var video = library.Asset(type: MediaType.Video, proxyTypes: [ProxyType.WebVideo]);
        var photo = library.LivePhoto(video);

        var page = await ReadPageAsync(library);

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            photo.Id,
            PairedMovId = video.Id,
            PairedMovContentHash = video.ContentHash,
            PairedMovHasVideo = true,
        });
    }

    [Fact]
    public async Task ReadPageAsync_SoftDeletedVideoOfAnActivePhoto_IsLeftOut()
    {
        var library = new Library();
        library.LivePhoto(library.Asset(type: MediaType.Video), reason: null);

        var page = await ReadPageAsync(library);

        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadPageAsync_SupersededVideo_IsAnItemOfItsOwn()
    {
        var library = new Library();
        var superseded = library.Asset(reason: SoftDeleteReason.Superseded, type: MediaType.Video, proxyTypes: [ProxyType.WebVideo]);
        superseded.IsLivePhotoVideo = true;
        library.LivePhoto(library.Asset(reason: null, type: MediaType.Video), reason: null);

        var page = await ReadPageAsync(library);

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            superseded.Id,
            Reason = SoftDeleteReason.Superseded,
            HasThumbnail = false,
            HasVideo = true,
        });
    }

    // ==========================================================================
    // Order and pages
    // ==========================================================================

    [Fact]
    public async Task ReadPageAsync_IsNewestLastSeenFirstThenByIdDescending()
    {
        var library = new Library();
        library.Asset(lastSeen: Monday).Id = Id(1);
        library.Asset(lastSeen: Monday + Duration.FromDays(1)).Id = Id(2);
        library.Asset(lastSeen: Monday).Id = Id(3);

        var page = await ReadPageAsync(library);

        page.Items.Select(view => view.Id).Should().Equal(Id(2), Id(3), Id(1));
    }

    [Fact]
    public async Task ReadPageAsync_FitsOnOnePage_HasNoNextCursor()
    {
        var library = new Library();
        library.Asset();
        library.Asset();

        var page = await ReadPageAsync(library, size: 2);

        page.Items.Should().HaveCount(2);
        page.Next.Should().BeNull();
    }

    [Fact]
    public async Task ReadPageAsync_MoreThanAPage_TakesThePageSizeAndPointsAtItsLastRow()
    {
        var library = new Library();
        foreach (var n in Enumerable.Range(1, 3))
            library.Asset(lastSeen: Monday + Duration.FromDays(n)).Id = Id(n);

        var page = await ReadPageAsync(library, size: 2);

        page.Items.Select(view => view.Id).Should().Equal(Id(3), Id(2));
        page.Next.Should().Be(new AssetCursor(Monday + Duration.FromDays(2), Id(2)));
    }

    [Fact]
    public async Task ReadPageAsync_AfterACursor_StartsWithTheNextRowInOrder()
    {
        var library = new Library();
        foreach (var n in Enumerable.Range(1, 4))
            library.Asset(lastSeen: n == 4 ? Monday - Duration.FromHours(1) : Monday).Id = Id(n);

        var page = await ReadPageAsync(library, new AssetCursor(Monday, Id(2)));

        page.Items.Select(view => view.Id).Should().Equal(Id(1), Id(4));
    }

    [Fact]
    public async Task ReadPageAsync_KeysetPages_CoverTheSetWithoutGapsOrRepeats()
    {
        var library = new Library();
        int[] hoursAfterMonday = [0, 0, 2, 2, 2, 1, 1];
        foreach (var (n, hours) in hoursAfterMonday.Select((hours, index) => (index + 1, hours)))
            library.Asset(lastSeen: Monday + Duration.FromHours(hours)).Id = Id(n);
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        var reader = new EfSoftDeletedAssetReader(db);

        var all = new List<Guid>();
        AssetCursor? after = null;
        do
        {
            var page = await reader.ReadPageAsync(library.Mira.Id, after, 2, TestContext.Current.CancellationToken);
            all.AddRange(page.Items.Select(view => view.Id));
            after = page.Next;
        }
        while (after is not null && all.Count < 10);

        all.Should().Equal(Id(5), Id(4), Id(3), Id(7), Id(6), Id(2), Id(1));
    }

    // ==========================================================================
    // Generated SQL
    // ==========================================================================

    [Fact]
    public void Query_PagesByKeysetInSqlWithoutTheSoftDeleteFilter()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfSoftDeletedAssetReader.Query(db, Guid.NewGuid(), new AssetCursor(Monday, Guid.NewGuid()), 61).ToQueryString();

        sql.Should().MatchRegex(@"\.""LastSeenOnNas"" < @cursor_At OR \(\w+\.""LastSeenOnNas"" = @cursor_At AND \w+\.""Id"" < @cursor_AssetId\)")
            .And.MatchRegex(@"ORDER BY \w+\.""LastSeenOnNas"" DESC, \w+\.""Id"" DESC\s+LIMIT @p");
        sql.Split("\"IsSoftDeleted\"").Should().HaveCount(2, "only the base predicate names it; no soft-delete filter reaches the joins");
    }
}
