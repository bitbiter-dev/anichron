using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class AssetViewReaderTests
{
    private sealed class Everything : IAssetSelection
    {
        public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets;
    }

    private sealed class OnlyAsset(Guid id) : IAssetSelection
    {
        public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets.Where(a => a.Id == id);
    }

    private sealed class Library
    {
        private readonly List<UserStorageConfig> configs = [];
        private readonly List<MediaAsset> assets = [];
        private readonly List<ProxyFile> proxies = [];
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
            UserStorageConfig config,
            LocalDateTime? captured = null,
            MediaType type = MediaType.Image,
            bool softDeleted = false,
            Metadata? metadata = null,
            params ProxyType[] proxyTypes)
        {
            var when = captured ?? new LocalDateTime(2019, 7, 14, 9, 30);
            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                StorageConfigId = config.Id,
                FilePath = $"{Guid.NewGuid():N}.jpg",
                FileName = "x.jpg",
                ContentHash = Guid.NewGuid().ToString("N"),
                DateCaptured = when,
                Year = when.Year,
                Month = when.Month,
                Day = when.Day,
                MediaType = type,
                IsSoftDeleted = softDeleted,
                Metadata = metadata,
            };
            assets.Add(asset);
            proxies.AddRange(proxyTypes.Select(t => new ProxyFile
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                ProxyType = t,
                ProxyPath = $"{asset.ContentHash}/{t}",
            }));
            return asset;
        }

        public void Interaction(User user, MediaAsset asset, bool starred = false, bool liked = false, bool hidden = false)
            => interactions.Add(new AssetInteraction
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                AssetId = asset.Id,
                IsStarred = starred,
                IsLiked = liked,
                IsHidden = hidden,
            });

        public async Task<DbContextOptions<AnichronDbContext>> SeedAsync()
        {
            var options = new DbContextOptionsBuilder<AnichronDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new AnichronDbContext(options);
            db.Users.AddRange(Mira, Alice);
            db.StorageConfigs.AddRange(configs);
            db.MediaAssets.AddRange(assets);
            db.ProxyFiles.AddRange(proxies);
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

    private static async Task<IReadOnlyList<AssetView>> ReadAsync(Library library, User caller, IAssetSelection? selection = null)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        return await new EfAssetViewReader(db)
            .ReadAsync(caller.Id, selection ?? new Everything(), TestContext.Current.CancellationToken);
    }

    // ==========================================================================
    // Visibility (#241) and the hidden rule (#48)
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_ReturnsOnlyTheCallersVisibleAssets()
    {
        var library = new Library();
        var own = library.Asset(library.MiraConfig);
        library.Asset(library.MiraConfig, softDeleted: true);
        library.Asset(library.MiraArchive);
        library.Asset(library.AliceConfig);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(own.Id);
    }

    [Fact]
    public async Task ReadAsync_AssetTheCallerHid_IsExcluded()
    {
        var library = new Library();
        var hidden = library.Asset(library.MiraConfig);
        var kept = library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, hidden, hidden: true);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(kept.Id);
    }

    [Fact]
    public async Task ReadAsync_AssetAnotherUserHid_IsStillReturned()
    {
        var library = new Library();
        var asset = library.Asset(library.MiraConfig);
        library.Interaction(library.Alice, asset, hidden: true);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(asset.Id);
    }

    [Fact]
    public async Task ReadAsync_AppliesTheSelectionAfterVisibility()
    {
        var library = new Library();
        library.Asset(library.MiraConfig);
        var wanted = library.Asset(library.MiraConfig);
        var alices = library.Asset(library.AliceConfig);

        (await ReadAsync(library, library.Mira, new OnlyAsset(wanted.Id))).Select(v => v.Id).Should().Equal(wanted.Id);
        (await ReadAsync(library, library.Mira, new OnlyAsset(alices.Id))).Should().BeEmpty();
    }

    // ==========================================================================
    // Interaction state
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_NoInteractionRow_IsNeitherStarredNorLiked()
    {
        var library = new Library();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.IsStarred.Should().BeFalse();
        view.IsLiked.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadAsync_CallersInteraction_IsReported(bool starred, bool liked)
    {
        var library = new Library();
        var asset = library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, asset, starred: starred, liked: liked);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.IsStarred.Should().Be(starred);
        view.IsLiked.Should().Be(liked);
    }

    [Fact]
    public async Task ReadAsync_AnotherUsersStarAndLike_AreNotShown()
    {
        var library = new Library();
        var asset = library.Asset(library.MiraConfig);
        library.Interaction(library.Alice, asset, starred: true, liked: true);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.IsStarred.Should().BeFalse();
        view.IsLiked.Should().BeFalse();
    }

    // ==========================================================================
    // Asset fields
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_MapsTheAssetMetadataAndProxies()
    {
        var library = new Library();
        var asset = library.Asset(
            library.MiraConfig,
            new LocalDateTime(2019, 7, 14, 9, 30),
            MediaType.Video,
            false,
            new Metadata { Width = 4032, Height = 3024, Orientation = 6, DurationInSeconds = 12, Blurhash = "LEHV6nWB2yk8" },
            ProxyType.Thumbnail,
            ProxyType.WebVideo);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.Should().Be(new AssetView(
            asset.Id, MediaType.Video, 2019, 7, 14, new LocalDateTime(2019, 7, 14, 9, 30),
            4032, 3024, 6, 12, "LEHV6nWB2yk8",
            HasThumbnail: true, HasPreview: false, HasVideo: true,
            PairedMovId: null, PairedMovHasVideo: false,
            IsStarred: false, IsLiked: false));
    }

    [Fact]
    public async Task ReadAsync_PreviewProxy_IsReported()
    {
        var library = new Library();
        library.Asset(library.MiraConfig, null, MediaType.Image, false, null, ProxyType.FullPreview);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasVideo).Should().Be((false, true, false));
    }

    [Fact]
    public async Task ReadAsync_NoMetadata_MapsToNulls()
    {
        var library = new Library();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.Width.Should().BeNull();
        view.Height.Should().BeNull();
        view.Orientation.Should().BeNull();
        view.DurationInSeconds.Should().BeNull();
        view.Blurhash.Should().BeNull();
    }

    // ==========================================================================
    // Live Photo pairing
    // ==========================================================================

    private static async Task<AssetView> LivePhotoAsync(bool movSoftDeleted, params ProxyType[] movProxies)
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video, softDeleted: movSoftDeleted, proxyTypes: movProxies);
        var heic = library.Asset(library.MiraConfig, type: MediaType.LivePhoto);
        heic.PairedAssetId = mov.Id;

        var views = await ReadAsync(library, library.Mira, new OnlyAsset(heic.Id));
        return views.Should().ContainSingle().Which;
    }

    [Fact]
    public async Task ReadAsync_PairedMovWithVideo_ReportsItsIdAndVideo()
    {
        var view = await LivePhotoAsync(movSoftDeleted: false, ProxyType.WebVideo);

        view.PairedMovId.Should().NotBeNull();
        view.PairedMovHasVideo.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_PairedMovWithoutVideo_ReportsItsIdButNoVideo()
    {
        var view = await LivePhotoAsync(movSoftDeleted: false, ProxyType.Thumbnail);

        view.PairedMovId.Should().NotBeNull();
        view.PairedMovHasVideo.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_SoftDeletedPairedMov_IsNotReported()
    {
        var view = await LivePhotoAsync(movSoftDeleted: true, ProxyType.WebVideo);

        view.PairedMovId.Should().BeNull();
        view.PairedMovHasVideo.Should().BeFalse();
    }

    // ==========================================================================
    // OnThisDay (#38)
    // ==========================================================================

    [Fact]
    public async Task OnThisDay_ReturnsTheSameDayInEarlierYearsOnly()
    {
        var library = new Library();
        var earlier = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var muchEarlier = library.Asset(library.MiraConfig, new LocalDateTime(2003, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2026, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2027, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 8, 14, 9, 0));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Select(v => v.Id).Should().BeEquivalentTo([earlier.Id, muchEarlier.Id]);
    }

    [Theory]
    [InlineData(2025, 2, 28, false)]
    [InlineData(2025, 3, 1, false)]
    [InlineData(2028, 2, 29, true)]
    public async Task OnThisDay_TwentyNinthOfFebruary_IsOnlyMatchedOnALeapDay(int year, int month, int day, bool matched)
    {
        var library = new Library();
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 9, 0));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(year, month, day)));

        views.Should().HaveCount(matched ? 1 : 0);
    }

    // ==========================================================================
    // Generated SQL
    // ==========================================================================

    private static AnichronDbContext CreateNpgsqlModelOnlyDb()
        => new(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql("Host=unused", o => o.UseNodaTime())
            .EnableServiceProviderCaching(false)
            .Options);

    [Fact]
    public void Query_OnThisDay_IsOneStatementOnTheIndexedIntegerColumns()
    {
        using var db = CreateNpgsqlModelOnlyDb();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new OnThisDay(new LocalDate(2026, 7, 14))).ToQueryString();

        sql.Should().Contain("\"Month\" = @Date_Month").And.Contain("@Date_Month='7'")
            .And.Contain("\"Day\" = @Date_Day").And.Contain("@Date_Day='14'")
            .And.Contain("\"Year\" < @Date_Year").And.Contain("@Date_Year='2026'")
            .And.NotContain("EXTRACT")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_ScopesTheHiddenRuleAndTheInteractionJoinToTheCaller()
    {
        using var db = CreateNpgsqlModelOnlyDb();
        var caller = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var sql = EfAssetViewReader.Query(db, caller, new Everything()).ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"IsHidden\"")
            .And.Contain("LEFT JOIN")
            .And.Contain("FROM \"Interactions\"")
            .And.Contain("11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public async Task ReadAsync_IsNotTracked()
    {
        var library = new Library();
        library.Asset(library.MiraConfig);
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);

        await new EfAssetViewReader(db).ReadAsync(library.Mira.Id, new Everything(), TestContext.Current.CancellationToken);

        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
