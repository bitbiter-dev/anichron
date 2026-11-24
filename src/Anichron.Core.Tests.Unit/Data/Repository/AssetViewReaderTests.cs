using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Testing.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.RegularExpressions;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed partial class AssetViewReaderTests
{
    private sealed class Everything(AssetSet set = AssetSet.Shown) : IAssetSelection
    {
        public AssetSet Set => set;

        public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets;
    }

    private sealed class OnlyAsset(Guid id) : IAssetSelection
    {
        public AssetSet Set => AssetSet.Shown;

        public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets.Where(a => a.Id == id);
    }

    private sealed class Library
    {
        private readonly List<UserStorageConfig> configs = [];
        private readonly List<MediaAsset> assets = [];
        private readonly List<ProxyFile> proxies = [];
        private readonly List<AssetInteraction> interactions = [];
        private readonly List<Burst> bursts = [];

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
                Captured = Recorded.At(when),
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

        public MediaAsset LivePhoto(MediaAsset mov, LocalDateTime? captured = null, bool softDeleted = false)
        {
            var heic = Asset(MiraConfig, captured, MediaType.LivePhoto, softDeleted);
            heic.PairedAssetId = mov.Id;
            return heic;
        }

        public void Interaction(
            User user, MediaAsset asset, bool starred = false, bool liked = false, bool hidden = false, Instant? hiddenAt = null)
            => interactions.Add(new AssetInteraction
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                AssetId = asset.Id,
                IsStarred = starred,
                IsLiked = liked,
                HiddenAt = hiddenAt ?? (hidden ? Instant.FromUtc(2026, 11, 1, 12, 0) : null),
            });

        public Burst Burst(params MediaAsset[] members)
        {
            var burst = new Burst { Id = Guid.NewGuid(), PrimaryAssetId = members[0].Id };
            bursts.Add(burst);
            foreach (var member in members)
                member.BurstId = burst.Id;
            return burst;
        }

        public async Task<DbContextOptions<AnichronDbContext>> SeedAsync()
        {
            var options = AnichronDbContexts.InMemoryOptions();
            await using var db = new AnichronDbContext(options);
            db.Users.AddRange(Mira, Alice);
            db.StorageConfigs.AddRange(configs);
            db.MediaAssets.AddRange(assets);
            db.ProxyFiles.AddRange(proxies);
            db.Interactions.AddRange(interactions);
            db.Bursts.AddRange(bursts);
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
            new Metadata { Description = MediaDescription.Unknown with { Width = 4032, Height = 3024, Orientation = 6, DurationInSeconds = 12 }, Blurhash = "LEHV6nWB2yk8" },
            ProxyType.Thumbnail,
            ProxyType.WebVideo);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.Should().Be(new AssetView(
            asset.Id, asset.ContentHash, MediaType.Video, 2019, 7, 14, new LocalDateTime(2019, 7, 14, 9, 30),
            4032, 3024, 6, 12, "LEHV6nWB2yk8",
            HasThumbnail: true, HasPreview: false, HasCompactPreview: false, HasVideo: true,
            PairedMovId: null, PairedMovContentHash: null, PairedMovHasVideo: false,
            IsStarred: false, IsLiked: false, BurstId: null, HiddenAt: null));
    }

    [Fact]
    public async Task ReadAsync_RowThatTookNewContent_KeepsItsIdAndReportsTheNewHash()
    {
        var library = new Library();
        var asset = library.Asset(library.MiraConfig, proxyTypes: ProxyType.Thumbnail);
        asset.ContentHash = "0ld0ld0ld0ld0ld0";
        var options = await library.SeedAsync();
        await using (var db = new AnichronDbContext(options))
        {
            var row = await db.MediaAssets.Include(a => a.ProxyFiles).SingleAsync(TestContext.Current.CancellationToken);
            row.ContentHash = "n3wn3wn3wn3wn3wn";
            row.ProxyFiles.Single().ProxyPath = "n3/n3wn3wn3wn3wn3wn/thumbnail.jpg";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = new AnichronDbContext(options);
        var views = await new EfAssetViewReader(reading)
            .ReadAsync(library.Mira.Id, new Everything(), TestContext.Current.CancellationToken);

        var view = views.Should().ContainSingle().Which;
        (view.Id, view.ContentHash).Should().Be((asset.Id, "n3wn3wn3wn3wn3wn"));
    }

    [Fact]
    public async Task ReadAsync_PreviewProxy_IsReported()
    {
        var library = new Library();
        library.Asset(library.MiraConfig, null, MediaType.Image, false, null, ProxyType.FullPreview);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasCompactPreview, view.HasVideo).Should().Be((false, true, false, false));
    }

    [Fact]
    public async Task ReadAsync_CompactPreviewProxy_IsReported()
    {
        var library = new Library();
        library.Asset(library.MiraConfig, null, MediaType.Image, false, null, ProxyType.CompactPreview);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasCompactPreview, view.HasVideo).Should().Be((false, false, true, false));
    }

    [Fact]
    public async Task ReadAsync_NoProxies_ReportsNone()
    {
        var library = new Library();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasCompactPreview, view.HasVideo).Should().Be((false, false, false, false));
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

    [Fact]
    public async Task ReadAsync_BurstMember_ReportsItsBurst()
    {
        var library = new Library();
        var member = library.Asset(library.MiraConfig);
        var burst = library.Burst(member, library.Asset(library.MiraConfig));

        var views = await ReadAsync(library, library.Mira, new OnlyAsset(member.Id));

        views.Should().ContainSingle().Which.BurstId.Should().Be(burst.Id);
    }

    [Fact]
    public async Task ReadAsync_OutsideABurst_ReportsNoBurst()
    {
        var library = new Library();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.BurstId.Should().BeNull();
    }

    // ==========================================================================
    // Live Photo pairing
    // ==========================================================================

    private const string MovHash = "cafebabecafebabe";

    private static async Task<AssetView> LivePhotoAsync(bool movSoftDeleted, params ProxyType[] movProxies)
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video, softDeleted: movSoftDeleted, proxyTypes: movProxies);
        mov.ContentHash = MovHash;
        var heic = library.LivePhoto(mov);

        var views = await ReadAsync(library, library.Mira, new OnlyAsset(heic.Id));
        return views.Should().ContainSingle().Which;
    }

    [Fact]
    public async Task ReadAsync_PairedMovWithVideo_ReportsItsIdHashAndVideo()
    {
        var view = await LivePhotoAsync(movSoftDeleted: false, ProxyType.WebVideo);

        view.PairedMovId.Should().NotBeNull();
        view.PairedMovContentHash.Should().Be(MovHash);
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
        view.PairedMovContentHash.Should().BeNull();
        view.PairedMovHasVideo.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_PairedMov_IsNotListedOnItsOwn()
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video, proxyTypes: ProxyType.WebVideo);
        var heic = library.LivePhoto(mov);
        var video = library.Asset(library.MiraConfig, type: MediaType.Video);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().BeEquivalentTo([heic.Id, video.Id]);
        views.Single(v => v.Id == heic.Id).PairedMovId.Should().Be(mov.Id);
    }

    [Fact]
    public async Task ReadAsync_MovOfAHiddenLivePhoto_IsNotListedOnItsOwn()
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        var heic = library.LivePhoto(mov);
        library.Interaction(library.Mira, heic, hidden: true);
        var kept = library.Asset(library.MiraConfig);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(kept.Id);
    }

    [Fact]
    public async Task ReadAsync_MovOfASoftDeletedLivePhoto_IsListedAsAVideo()
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        library.LivePhoto(mov, softDeleted: true);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(mov.Id);
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
    [InlineData(2025, 2, 28, true)]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2028, 2, 28, false)]
    [InlineData(2025, 3, 1, false)]
    [InlineData(2027, 2, 27, false)]
    public async Task OnThisDay_TwentyNinthOfFebruary_IsMatchedOnALeapDayAndOnTheTwentyEighthOfOtherYears(
        int year, int month, int day, bool matched)
    {
        var library = new Library();
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 9, 0));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(year, month, day)));

        views.Should().HaveCount(matched ? 1 : 0);
    }

    [Fact]
    public async Task OnThisDay_TwentyEighthOfFebruaryInAYearWithoutALeapDay_BringsBothDaysOnly()
    {
        var library = new Library();
        var leapDay = library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 9, 0));
        var twentyEighth = library.Asset(library.MiraConfig, new LocalDateTime(2025, 2, 28, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2025, 2, 27, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2025, 3, 1, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 3, 29, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 1, 29, 9, 0));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2027, 2, 28)));

        views.Select(v => v.Id).Should().BeEquivalentTo([leapDay.Id, twentyEighth.Id]);
    }

    [Theory]
    [InlineData(3, 28, 29)]
    [InlineData(2, 14, 15)]
    public async Task OnThisDay_AnotherDayInAYearWithoutALeapDay_DoesNotBringTheNextDay(int month, int day, int nextDay)
    {
        var library = new Library();
        var onTheDay = library.Asset(library.MiraConfig, new LocalDateTime(2019, month, day, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, month, nextDay, 9, 0));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2027, month, day)));

        views.Select(v => v.Id).Should().Equal(onTheDay.Id);
    }

    [Theory]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2027, 3, 1, false)]
    public async Task OnThisDay_BurstCoveredOnTheTwentyNinthOfFebruary_FallsOnTheDayItIsSelectedFor(
        int year, int month, int day, bool kept)
    {
        var library = new Library();
        var cover = library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 23, 59));
        var afterMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2024, 3, 1, 0, 1));
        library.Burst(cover, afterMidnight);
        var selection = new OnThisDay(new LocalDate(year, month, day));

        var views = await ReadAsync(library, library.Mira, selection);

        views.Select(v => v.Id).Should().BeEquivalentTo([cover.Id, afterMidnight.Id]);
        selection.FallsOnTheDay(views.Single(v => v.Id == cover.Id)).Should().Be(kept);
    }

    [Theory]
    [InlineData(3, 28, 29)]
    [InlineData(2, 14, 15)]
    public async Task FallsOnTheDay_AnotherDayInAYearWithoutALeapDay_IsNotTheNextDay(int month, int day, int nextDay)
    {
        var library = new Library();
        var beforeMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, month, day, 23, 59));
        var afterMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, month, nextDay, 0, 1));
        library.Burst(beforeMidnight, afterMidnight);
        var selection = new OnThisDay(new LocalDate(2027, month, day));

        var views = await ReadAsync(library, library.Mira, selection);

        views.Select(v => (v.Id, selection.FallsOnTheDay(v))).Should().BeEquivalentTo(
            [(beforeMidnight.Id, true), (afterMidnight.Id, false)]);
    }

    [Fact]
    public async Task OnThisDay_BurstWithAMemberOnTheDay_BringsEveryMemberButNoOtherAssets()
    {
        var library = new Library();
        var beforeMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 23, 59));
        var afterMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 0, 1));
        library.Burst(beforeMidnight, afterMidnight);
        var single = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 9, 0));
        var otherBurst = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 10, 0));
        library.Burst(otherBurst, library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 10, 1)));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Select(v => v.Id).Should().BeEquivalentTo([beforeMidnight.Id, afterMidnight.Id, single.Id]);
    }

    [Fact]
    public async Task OnThisDay_UndatedBurstMember_IsLeftOut()
    {
        var library = new Library();
        var dated = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var undated = library.Asset(library.MiraConfig);
        undated.Captured = null;
        library.Burst(dated, undated);

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Should().ContainSingle().Which.Id.Should().Be(dated.Id);
    }

    [Theory]
    [InlineData(2026, 7, 14)]
    [InlineData(2027, 7, 14)]
    [InlineData(2019, 8, 14)]
    [InlineData(2019, 7, 15)]
    public async Task OnThisDay_BurstWithoutAMemberOnTheDayInAnEarlierYear_IsNotBroughtIn(int year, int month, int day)
    {
        var library = new Library();
        library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(year, month, day, 12, 0)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 16, 12, 0)));

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Should().BeEmpty();
    }

    [Fact]
    public async Task OnThisDay_OnlyMemberOnTheDayIsHidden_DoesNotBringItsBurst()
    {
        var library = new Library();
        var hidden = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 23, 59));
        library.Burst(hidden, library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 0, 1)));
        library.Interaction(library.Mira, hidden, hidden: true);

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Should().BeEmpty();
    }

    [Fact]
    public async Task OnThisDay_EveryMemberBeforeMidnightHidden_BringsTheBurstOnTheNextDay()
    {
        var library = new Library();
        var hidden = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 23, 59));
        var afterMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 0, 1));
        library.Burst(hidden, afterMidnight);
        library.Interaction(library.Mira, hidden, hidden: true);

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 15)));

        views.Select(v => v.Id).Should().Equal(afterMidnight.Id);
    }

    [Fact]
    public async Task OnThisDay_TwoMembersOnTheDay_BringEachMemberOnce()
    {
        var library = new Library();
        var first = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var second = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 1));
        library.Burst(first, second);

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Select(v => v.Id).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task OnThisDay_LivePhoto_IsListedOnceWithItsMov()
    {
        var library = new Library();
        var onTheDay = new LocalDateTime(2019, 7, 14, 9, 0);
        var mov = library.Asset(library.MiraConfig, onTheDay, MediaType.Video, proxyTypes: ProxyType.WebVideo);
        var heic = library.LivePhoto(mov, onTheDay);
        var video = library.Asset(library.MiraConfig, onTheDay, MediaType.Video);

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        views.Select(v => v.Id).Should().BeEquivalentTo([heic.Id, video.Id]);
        var livePhoto = views.Single(v => v.Id == heic.Id);
        (livePhoto.PairedMovId, livePhoto.PairedMovHasVideo).Should().Be((mov.Id, true));
    }

    [Fact]
    public async Task OnThisDay_MovWhoseLivePhotoRowIsGone_IsListedAsAVideo()
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), MediaType.Video);

        var views = await ReadAsync(library, library.Mira, new OnThisDay(new LocalDate(2026, 7, 14)));

        var view = views.Should().ContainSingle().Which;
        (view.Id, view.MediaType).Should().Be((mov.Id, MediaType.Video));
    }

    [Theory]
    [InlineData(2019, 7, 14, true)]
    [InlineData(2019, 7, 13, false)]
    [InlineData(2019, 6, 14, false)]
    public void OnThisDay_FallsOnTheDay_ComparesMonthAndDay(int year, int month, int day, bool falls)
    {
        var view = new AssetView(
            Guid.NewGuid(), "0123456789abcdef", MediaType.Image, year, month, day, new LocalDateTime(year, month, day, 9, 0),
            null, null, null, null, null, false, false, false, false, null, null, false, false, false, null, null);

        new OnThisDay(new LocalDate(2026, 7, 14)).FallsOnTheDay(view).Should().Be(falls);
    }

    // ==========================================================================
    // BurstMembers (#299)
    // ==========================================================================

    [Fact]
    public async Task BurstMembers_ReturnsTheBurstsVisibleMembersOnly()
    {
        var library = new Library();
        var first = library.Asset(library.MiraConfig);
        var second = library.Asset(library.MiraConfig);
        var hidden = library.Asset(library.MiraConfig);
        var deleted = library.Asset(library.MiraConfig, softDeleted: true);
        var burst = library.Burst(first, second, hidden, deleted);
        library.Interaction(library.Mira, hidden, hidden: true);
        library.Burst(library.Asset(library.MiraConfig), library.Asset(library.MiraConfig));
        library.Asset(library.MiraConfig);

        var views = await ReadAsync(library, library.Mira, new BurstMembers(burst.Id));

        views.Select(v => v.Id).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task BurstMembers_PairedMovInTheBurst_IsNotListed()
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        var heic = library.LivePhoto(mov);
        var other = library.Asset(library.MiraConfig);
        var burst = library.Burst(heic, other, mov);

        var views = await ReadAsync(library, library.Mira, new BurstMembers(burst.Id));

        views.Select(v => v.Id).Should().BeEquivalentTo([heic.Id, other.Id]);
    }

    [Fact]
    public async Task BurstMembers_UnknownBurst_IsEmpty()
    {
        var library = new Library();
        library.Burst(library.Asset(library.MiraConfig), library.Asset(library.MiraConfig));

        (await ReadAsync(library, library.Mira, new BurstMembers(Guid.NewGuid()))).Should().BeEmpty();
    }

    [Fact]
    public async Task BurstMembers_AnotherUsersBurst_IsEmpty()
    {
        var library = new Library();
        var burst = library.Burst(library.Asset(library.AliceConfig), library.Asset(library.AliceConfig));

        (await ReadAsync(library, library.Mira, new BurstMembers(burst.Id))).Should().BeEmpty();
    }

    // ==========================================================================
    // HiddenAssets (#558)
    // ==========================================================================

    private static readonly Instant Monday = Instant.FromUtc(2026, 11, 16, 9, 0);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static async Task<List<AssetView>> AllPagesAsync(Library library, User caller, int take)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        var reader = new EfAssetViewReader(db);
        var all = new List<AssetView>();
        AssetCursor? after = null;
        for (var pages = 0; pages < 10; pages++)
        {
            var page = await reader.ReadAsync(caller.Id, new HiddenAssets(after, take), TestContext.Current.CancellationToken);
            all.AddRange(page);
            if (page.Count < take)
                return all;
            after = new AssetCursor(page[^1].HiddenAt!.Value, page[^1].Id);
        }

        throw new InvalidOperationException("The pages never ended.");
    }

    [Fact]
    public async Task HiddenAssets_ReturnsTheCallersHiddenAssetsNewestHiddenFirstWithWhenTheyWereHidden()
    {
        var library = new Library();
        var older = library.Asset(library.MiraConfig);
        var newest = library.Asset(library.MiraConfig);
        var middle = library.Asset(library.MiraConfig);
        library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, older, hiddenAt: Monday);
        library.Interaction(library.Mira, newest, hiddenAt: Monday + Duration.FromDays(2));
        library.Interaction(library.Mira, middle, starred: true, hiddenAt: Monday + Duration.FromDays(1));

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        views.Select(v => (v.Id, v.HiddenAt)).Should().Equal(
            (newest.Id, Monday + Duration.FromDays(2)),
            (middle.Id, Monday + Duration.FromDays(1)),
            (older.Id, Monday));
        views[1].IsStarred.Should().BeTrue();
    }

    [Fact]
    public async Task HiddenAssets_SameHiddenTime_IsOrderedByIdDescending()
    {
        var library = new Library();
        var first = library.Asset(library.MiraConfig);
        first.Id = Id(1);
        var second = library.Asset(library.MiraConfig);
        second.Id = Id(2);
        library.Interaction(library.Mira, first, hiddenAt: Monday);
        library.Interaction(library.Mira, second, hiddenAt: Monday);

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        views.Select(v => v.Id).Should().Equal(Id(2), Id(1));
    }

    [Fact]
    public async Task HiddenAssets_TakesAtMostThePageSize()
    {
        var library = new Library();
        foreach (var day in Enumerable.Range(0, 3))
            library.Interaction(library.Mira, library.Asset(library.MiraConfig), hiddenAt: Monday + Duration.FromDays(day));

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 2));

        views.Select(v => v.HiddenAt).Should().Equal(Monday + Duration.FromDays(2), Monday + Duration.FromDays(1));
    }

    [Fact]
    public async Task HiddenAssets_AfterACursor_StartsWithTheNextRowInOrder()
    {
        var library = new Library();
        foreach (var n in Enumerable.Range(1, 4))
        {
            var asset = library.Asset(library.MiraConfig);
            asset.Id = Id(n);
            library.Interaction(library.Mira, asset, hiddenAt: n == 4 ? Monday - Duration.FromHours(1) : Monday);
        }

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(new AssetCursor(Monday, Id(2)), Take: 10));

        views.Select(v => v.Id).Should().Equal(Id(1), Id(4));
    }

    [Fact]
    public async Task HiddenAssets_KeysetPages_CoverTheSetWithoutGapsOrRepeats()
    {
        var library = new Library();
        int[] hoursAfterMonday = [0, 0, 2, 2, 2, 1, 1];
        foreach (var (n, hours) in hoursAfterMonday.Select((hours, index) => (index + 1, hours)))
        {
            var asset = library.Asset(library.MiraConfig);
            asset.Id = Id(n);
            library.Interaction(library.Mira, asset, hiddenAt: Monday + Duration.FromHours(hours));
        }

        var all = await AllPagesAsync(library, library.Mira, take: 2);

        all.Select(v => v.Id).Should().Equal(Id(5), Id(4), Id(3), Id(7), Id(6), Id(2), Id(1));
    }

    [Fact]
    public async Task HiddenAssets_LeavesOutWhatIsNotTheCallersVisibleDatedHiddenAsset()
    {
        var library = new Library();
        var hidden = library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, hidden, hidden: true);
        library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, library.Asset(library.MiraConfig), starred: true);
        library.Interaction(library.Alice, library.Asset(library.MiraConfig), hidden: true);
        library.Interaction(library.Alice, library.Asset(library.AliceConfig), hidden: true);
        library.Interaction(library.Mira, library.Asset(library.MiraArchive), hidden: true);
        library.Interaction(library.Mira, library.Asset(library.MiraConfig, softDeleted: true), hidden: true);
        var undated = library.Asset(library.MiraConfig);
        undated.Captured = null;
        library.Interaction(library.Mira, undated, hidden: true);

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        views.Select(v => v.Id).Should().Equal(hidden.Id);
    }

    [Fact]
    public async Task HiddenAssets_HiddenLivePhoto_IsListedWithoutItsPairedMov()
    {
        var library = new Library();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        var heic = library.LivePhoto(mov);
        library.Interaction(library.Mira, heic, hidden: true);
        library.Interaction(library.Mira, mov, hidden: true);

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        var view = views.Should().ContainSingle().Which;
        (view.Id, view.PairedMovId).Should().Be((heic.Id, mov.Id));
    }

    [Fact]
    public async Task ReadAsync_ShownSet_ReportsNoHiddenTime()
    {
        var library = new Library();
        library.Interaction(library.Mira, library.Asset(library.MiraConfig), starred: true);

        var views = await ReadAsync(library, library.Mira);

        views.Should().ContainSingle().Which.HiddenAt.Should().BeNull();
    }

    // ==========================================================================
    // Generated SQL
    // ==========================================================================

    [Fact]
    public void Query_OnThisDay_IsOneStatementOnTheIndexedIntegerColumns()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new OnThisDay(new LocalDate(2026, 7, 14))).ToQueryString();

        sql.Should().Contain("\"Month\" = @Date_Month").And.Contain("@Date_Month='7'")
            .And.Contain("\"Day\" >= @Date_Day").And.Contain("@Date_Day='14'")
            .And.Contain("\"Day\" <= @lastDay").And.Contain("@lastDay='14'")
            .And.Contain("\"Year\" < @Date_Year").And.Contain("@Date_Year='2026'")
            .And.NotContain("EXTRACT")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_OnThisDay_BringsBurstMembersThroughTheCallersVisibleAssetsOnTheDay()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new OnThisDay(new LocalDate(2026, 7, 14))).ToQueryString();

        sql.Should().Contain("UNION")
            .And.Contain("\"BurstId\" IS NOT NULL")
            .And.NotContain(";");
        sql.Split("\"Month\" = @Date_Month AND").Should().HaveCount(3,
            "both arms of the union start from the indexed day columns, the members' arm joining on BurstId");
        sql.Split("\"UserId\" = @").Should().HaveCount(11,
            "the burst lookup applies the owned-config check, the hidden rule and the paired-MOV exclusion to the caller as well");
    }

    [Fact]
    public void Query_HiddenAssets_PagesByKeysetInSqlAndKeepsTheOrderOutermost()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new HiddenAssets(new AssetCursor(Monday, Id(1)), 61)).ToQueryString();

        sql.Should().MatchRegex(@"\.""HiddenAt"" < @cursor_At OR \(\w+\.""HiddenAt"" = @cursor_At AND \w+\.""Id"" < @cursor_AssetId\)")
            .And.MatchRegex(@"ORDER BY \w+\.""HiddenAt"" DESC, \w+\.""Id"" DESC\s+LIMIT @p")
            .And.MatchRegex(@"ORDER BY \w+\.""HiddenAt"" DESC, \w+\.""Id"" DESC$", "the joins around the page must not lose its order")
            .And.Contain("\"HiddenAt\" IS NOT NULL")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_HiddenAssets_FirstPageHasNoKeyset()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new HiddenAssets(After: null, 61)).ToQueryString();

        sql.Should().NotContain("@cursor").And.Contain("LIMIT @p");
    }

    [Fact]
    public void Query_BurstMembers_FiltersOnTheBurstAfterVisibility()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new BurstMembers(Guid.NewGuid())).ToQueryString();

        sql.Should().Contain("\"BurstId\" = @BurstId")
            .And.Contain("\"IsActive\"")
            .And.Contain("\"HiddenAt\" IS NOT NULL")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_OnThisDay_ExcludesPairedMovsInTheSameStatement()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new OnThisDay(new LocalDate(2026, 7, 14))).ToQueryString();

        PairedMovExclusions(sql).Should().Be(3,
            "both arms of the union and the burst lookup leave out a row a visible Live Photo points at");
        sql.Should().NotContain(";");
    }

    [Fact]
    public void Query_BurstMembers_ExcludesPairedMovsInTheSameStatement()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new BurstMembers(Guid.NewGuid())).ToQueryString();

        PairedMovExclusions(sql).Should().Be(1);
        sql.Should().NotContain(";");
    }

    private static int PairedMovExclusions(string sql) => PairedMovExclusion().Count(sql);

    [GeneratedRegex(@"""PairedAssetId"" = \w+\.""Id""\)")]
    private static partial Regex PairedMovExclusion();

    [Fact]
    public void Query_FiltersVisibilityTheHiddenRuleAndTheInteractionJoinOnTheCaller()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new Everything()).ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"IsActive\"")
            .And.Contain("\"IsSoftDeleted\"")
            .And.Contain("\"HiddenAt\" IS NOT NULL")
            .And.Contain("LEFT JOIN")
            .And.Contain("FROM \"Interactions\"");
        sql.Split("\"UserId\" = @").Should().HaveCount(5,
            "the owned-config check, the hidden rule, the paired-MOV exclusion and the interaction join each filter on the caller");
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
