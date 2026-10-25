using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Data;
using Anichron.Worker.Bursts;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Bursts;

public sealed class BurstDetectionTests : IAsyncDisposable
{
    private const string Root = "/data/originals/mira";
    private const string BurstA = "3F2504E0-4F89-11D3-9A0C-0305E82C3301";
    private const string BurstB = "9C1B7E44-0D2A-4C55-8F6B-2E1D3A4B5C6D";

    private static readonly Guid MiraId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ConfigId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid OtherConfigId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 23, 9, 0);
    private static readonly LocalDateTime Noon = new(2024, 7, 15, 12, 0, 0);

    private readonly DbContextOptions<AnichronDbContext> options = AnichronDbContexts.InMemoryOptions();
    private readonly AnichronDbContext db;
    private readonly IOriginalPhotoReader reader = Substitute.For<IOriginalPhotoReader>();
    private readonly IThumbnailMatcher matcher = Substitute.For<IThumbnailMatcher>();
    private readonly Dictionary<(string, string), double> scores = [];
    private readonly MockFileSystem fileSystem = new();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly IGuidFactory guids = Substitute.For<IGuidFactory>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly FakeLogger<BurstDetectionHandler> logger = new();
    private readonly FakeLogger<ActiveStorageConfigs> activeLogger = new();
    private readonly ScriptedBursts repository;
    private int nextGuid;

    public BurstDetectionTests()
    {
        db = new AnichronDbContext(options);
        repository = new ScriptedBursts(new EfBurstRepository(db));
        fileSystem.AddDirectory(Root);
        clock.GetCurrentInstant().Returns(Now);
        guids.NewGuid().Returns(_ => Guid.Parse($"bbbbbbbb-0000-0000-0000-{++nextGuid:D12}"));
        reader.Read(Arg.Any<string>()).Returns(new OriginalPhotoFacts(Noon, null));
        matcher.ScoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => scores.GetValueOrDefault((call.ArgAt<string>(0), call.ArgAt<string>(1))));
        Seed(new User { Id = MiraId, Username = "mira", Email = "mira@example.com", PasswordHash = "h" });
        Seed(new UserStorageConfig { Id = ConfigId, UserId = MiraId, RootPath = Root, IsActive = true });
        Seed(new UserStorageConfig { Id = OtherConfigId, UserId = MiraId, RootPath = "/data/originals/other", IsActive = true });
    }

    public async ValueTask DisposeAsync() => await db.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Seed(params object[] entities)
    {
        using var seed = new AnichronDbContext(options);
        seed.AddRange(entities);
        seed.SaveChanges();
    }

    // A candidate unless told otherwise: a thumbnail, a camera make and model, captured minutes after noon.
    private MediaAsset Photo(
        string name,
        int minutes = 0,
        int seconds = 0,
        MediaType type = MediaType.Image,
        Guid? config = null,
        bool thumbnail = true,
        string? make = "Apple",
        string? model = "iPhone 15 Pro",
        bool softDeleted = false,
        Guid? burstId = null)
    {
        var id = Guid.NewGuid();
        var asset = new MediaAsset
        {
            Id = id,
            StorageConfigId = config ?? ConfigId,
            FilePath = $"2024/{name}",
            FileName = name,
            ContentHash = name,
            MediaType = type,
            DateCaptured = Noon.PlusMinutes(minutes).PlusSeconds(seconds),
            IsSoftDeleted = softDeleted,
            BurstId = burstId,
            Metadata = new Metadata { AssetId = id, CameraMake = make, CameraModel = model },
            ProxyFiles = thumbnail
                ? [new ProxyFile { Id = Guid.NewGuid(), AssetId = id, ProxyType = ProxyType.Thumbnail, ProxyPath = name }]
                : [],
        };
        Seed(asset);
        return asset;
    }

    // Thumbnails are named after their photo; a pair scores the same whichever is compared first.
    private void Similar(MediaAsset a, MediaAsset b, double score = 0.5)
    {
        scores[(a.FileName, b.FileName)] = score;
        scores[(b.FileName, a.FileName)] = score;
    }

    private int Comparisons()
        => matcher.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IThumbnailMatcher.ScoreAsync));

    private void Labelled(MediaAsset photo, string? label)
        => reader.Read($"{Root}/{photo.FilePath}").Returns(new OriginalPhotoFacts(Noon, label));

    private void Checked(MediaAsset photo, string? label, bool candidate = true)
        => Seed(new BurstCheck { AssetId = photo.Id, CheckedAt = Now, IsCandidate = candidate, CameraBurstId = label });

    private Guid StoredBurst(int createdMinutesAgo, params MediaAsset[] members)
    {
        var id = Guid.NewGuid();
        Seed(new Burst { Id = id, PrimaryAssetId = members.FirstOrDefault()?.Id, CreatedAt = Now.Minus(Duration.FromMinutes(createdMinutesAgo)) });
        using var seed = new AnichronDbContext(options);
        foreach (var member in members)
            seed.MediaAssets.IgnoreQueryFilters().Single(a => a.Id == member.Id).BurstId = id;
        seed.SaveChanges();
        return id;
    }

    private void ClearCover(Guid burstId)
    {
        using var seed = new AnichronDbContext(options);
        seed.Bursts.IgnoreQueryFilters().Single(b => b.Id == burstId).PrimaryAssetId = null;
        seed.SaveChanges();
    }

    private Task<object?> RunAsync() => RunForAsync(ConfigId, Ct);

    private Task<object?> RunForAsync(Guid configId, CancellationToken ct)
        => new BurstDetectionHandler(
                new ActiveStorageConfigs(new EfUserStorageConfigRepository(db), activeLogger),
                repository, db, reader, matcher, fileSystem, clock, guids, Options.Create(new WorkerSettings()), logger)
            .RunAsync(new BurstDetectionPayload(configId), progress, ct);

    private Dictionary<string, Guid?> BurstsOf(params MediaAsset[] photos)
    {
        using var read = new AnichronDbContext(options);
        var ids = photos.Select(p => p.Id).ToList();
        var stored = read.MediaAssets.IgnoreQueryFilters().Where(a => ids.Contains(a.Id)).ToDictionary(a => a.Id, a => a.BurstId);
        return photos.ToDictionary(p => p.FileName, p => stored[p.Id]);
    }

    private List<Burst> StoredBursts()
    {
        using var read = new AnichronDbContext(options);
        return [.. read.Bursts.IgnoreQueryFilters().OrderBy(b => b.CreatedAt)];
    }

    private List<BurstCheck> StoredChecks()
    {
        using var read = new AnichronDbContext(options);
        return [.. read.BurstChecks.IgnoreQueryFilters()];
    }

    // ==========================================================================
    // Skipping and failing
    // ==========================================================================

    [Fact]
    public async Task RunAsync_InactiveConfig_SkipsWithoutReadingAnythingAsync()
    {
        var inactive = Guid.NewGuid();
        Seed(new UserStorageConfig { Id = inactive, UserId = MiraId, RootPath = "/data/originals/off", IsActive = false });
        Photo("a.heic", config: inactive);

        var result = await RunForAsync(inactive, Ct);

        result.Should().Be(new StorageConfigSkipped("The storage config is missing or inactive."));
        reader.DidNotReceiveWithAnyArgs().Read(default!);
        StoredChecks().Should().BeEmpty();
        activeLogger.Collector.GetSnapshot().Should().ContainSingle().Which.Message.Should().Be(
            $"Skipping BurstDetection of storage config {inactive}: it is missing or inactive.");
    }

    [Fact]
    public async Task RunAsync_ConfigGone_SkipsAsync()
        => (await RunForAsync(Guid.NewGuid(), Ct)).Should().BeOfType<StorageConfigSkipped>();

    [Fact]
    public async Task RunAsync_RootMissing_FailsWithoutMarkingAnyPhotoAsync()
    {
        Photo("a.heic");
        fileSystem.Directory.Delete(Root);

        var act = () => RunAsync();

        await act.Should().ThrowAsync<DirectoryNotFoundException>().WithMessage($"*{Root}*");
        StoredChecks().Should().BeEmpty();
    }

    // ==========================================================================
    // Camera-labelled grouping
    // ==========================================================================

    [Fact]
    public async Task RunAsync_PhotosWithOneBurstUuid_FormOneBurstWithTheEarliestAsCoverAsync()
    {
        var (a, b, c) = (Photo("a.heic", minutes: 2), Photo("b.heic", minutes: 1), Photo("c.heic", minutes: 3));
        foreach (var photo in new[] { a, b, c })
            Labelled(photo, BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 3, Created = 1 });
        var burst = StoredBursts().Should().ContainSingle().Which;
        burst.Should().BeEquivalentTo(new { PrimaryAssetId = b.Id, CreatedAt = Now });
        BurstsOf(a, b, c).Values.Should().AllBeEquivalentTo(burst.Id);
    }

    [Fact]
    public async Task RunAsync_SameBurstUuidDaysApart_StillOneBurstAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", minutes: 3 * 24 * 60));
        Labelled(a, BurstA);
        Labelled(b, BurstA);

        await RunAsync();

        StoredBursts().Should().ContainSingle().Which.PrimaryAssetId.Should().Be(a.Id);
    }

    [Fact]
    public async Task RunAsync_TwoBurstUuids_TwoBurstsAndUnlabelledPhotosStayAloneAsync()
    {
        var (a1, a2, b1, b2, plain) = (Photo("a1.heic"), Photo("a2.heic", 1), Photo("b1.heic", 2), Photo("b2.heic", 3), Photo("p.heic", 4));
        Labelled(a1, BurstA);
        Labelled(a2, BurstA);
        Labelled(b1, BurstB);
        Labelled(b2, BurstB);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 5, Created = 2, NotMatched = 5 });
        var bursts = BurstsOf(a1, a2, b1, b2, plain);
        var (burstA, burstB) = (bursts["a1.heic"], bursts["b1.heic"]);
        new[] { burstA, burstB }.Should().OnlyHaveUniqueItems().And.NotContainNulls();
        bursts.Should().BeEquivalentTo(new Dictionary<string, Guid?>
        {
            ["a1.heic"] = burstA,
            ["a2.heic"] = burstA,
            ["b1.heic"] = burstB,
            ["b2.heic"] = burstB,
            ["p.heic"] = null,
        });
    }

    [Fact]
    public async Task RunAsync_LonePhotoWithABurstUuid_FormsNoBurstAsync()
    {
        var a = Photo("a.heic");
        Labelled(a, BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1 });
        StoredBursts().Should().BeEmpty();
        BurstsOf(a)["a.heic"].Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_SameBurstUuidInAnotherStorageConfig_IsNotJoinedAsync()
    {
        var mine = Photo("a.heic");
        var theirs = Photo("b.heic", config: OtherConfigId);
        Checked(theirs, BurstA);
        Labelled(mine, BurstA);

        await RunAsync();

        StoredBursts().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_LivePhotoHalfJoins_VideosAndPairedMovsAreNeverCheckedAsync()
    {
        var still = Photo("a.jpg");
        var live = Photo("b.heic", minutes: 1, type: MediaType.LivePhoto);
        var mov = Photo("b.mov", minutes: 1, type: MediaType.Video);
        Labelled(still, BurstA);
        Labelled(live, BurstA);
        Labelled(mov, BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 2, Created = 1 });
        BurstsOf(still, live, mov).Should().BeEquivalentTo(new Dictionary<string, Guid?>
        {
            ["a.jpg"] = StoredBursts().Single().Id,
            ["b.heic"] = StoredBursts().Single().Id,
            ["b.mov"] = null,
        });
        StoredChecks().Select(c => c.AssetId).Should().BeEquivalentTo([still.Id, live.Id]);
        reader.DidNotReceive().Read($"{Root}/{mov.FilePath}");
    }

    [Theory]
    [InlineData(false, "Apple", "iPhone 15 Pro", true)]
    [InlineData(true, null, "iPhone 15 Pro", true)]
    [InlineData(true, "Apple", null, true)]
    [InlineData(true, "Apple", "iPhone 15 Pro", false)]
    public async Task RunAsync_PhotoLackingWhatACandidateNeeds_IsCheckedButNotGroupedAsync(
        bool exifTime, string? make, string? model, bool thumbnail)
    {
        var good = Photo("a.heic");
        var lacking = Photo("b.heic", minutes: 1, thumbnail: thumbnail, make: make, model: model);
        Labelled(good, BurstA);
        reader.Read($"{Root}/{lacking.FilePath}").Returns(new OriginalPhotoFacts(exifTime ? Noon : null, BurstA));
        var other = Photo("c.heic", minutes: 2);
        Labelled(other, BurstA);

        await RunAsync();

        BurstsOf(lacking)["b.heic"].Should().BeNull();
        StoredChecks().Single(c => c.AssetId == lacking.Id)
            .Should().BeEquivalentTo(new { IsCandidate = false, CameraBurstId = BurstA, CheckedAt = Now });
        StoredBursts().Should().ContainSingle().Which.PrimaryAssetId.Should().Be(good.Id);
    }

    [Fact]
    public async Task RunAsync_LabelledPhotoAlreadyCheckedAsNoCandidate_IsNotJoinedAsync()
    {
        var old = Photo("a.heic");
        Checked(old, BurstA, candidate: false);
        var fresh = Photo("b.heic", minutes: 1);
        Labelled(fresh, BurstA);

        await RunAsync();

        StoredBursts().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnreadableOriginal_IsCheckedAsNoCandidateAsync()
    {
        var a = Photo("a.heic");
        reader.Read($"{Root}/{a.FilePath}").Returns(OriginalPhotoFacts.Unknown);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1 });
        StoredChecks().Should().ContainSingle().Which.Should().BeEquivalentTo(
            new BurstCheck { AssetId = a.Id, CheckedAt = Now, IsCandidate = false, CameraBurstId = null },
            o => o.Excluding(c => c.Asset));
    }

    // ==========================================================================
    // Similarity grouping
    // ==========================================================================

    [Fact]
    public async Task RunAsync_SimilarPhotoWithinTheGap_FormsABurstWithTheEarliestAsCoverAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        Similar(a, b);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 2, Created = 1, Matched = 1 });
        var burst = StoredBursts().Should().ContainSingle().Which;
        burst.PrimaryAssetId.Should().Be(a.Id);
        BurstsOf(a, b).Values.Should().AllBeEquivalentTo(burst.Id);
    }

    [Theory]
    [InlineData(0.25, 1)]
    [InlineData(0.2499, 0)]
    public async Task RunAsync_ScoreAtTheThreshold_MatchesAndBelowItDoesNotAsync(double score, int bursts)
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        Similar(a, b, score);

        var result = await RunAsync();

        result.Should().BeEquivalentTo(new { Matched = bursts, NotMatched = 1 - bursts });
        StoredBursts().Should().HaveCount(bursts);
    }

    [Fact]
    public async Task RunAsync_DissimilarPhotosSecondsApart_StayAloneAsync()
    {
        Photo("a.heic");
        Photo("b.heic", seconds: 3);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 2, NotMatched = 1 });
        StoredBursts().Should().BeEmpty();
        repository.BurstLoads.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ComparesTheNewerPhotoWithTheOlderOnceWithTheirThumbnailsAsync()
    {
        Photo("a.heic");
        Photo("b.heic", 1);

        await RunAsync();

        Comparisons().Should().Be(1);
        await matcher.Received(1).ScoreAsync("b.heic", "a.heic", Ct);
    }

    [Theory]
    [InlineData(120, 1)]
    [InlineData(121, 0)]
    public async Task RunAsync_NeighbourAtTheGap_IsComparedAndBeyondItIsNotAsync(int seconds, int bursts)
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", seconds: seconds));
        Similar(a, b);

        await RunAsync();

        StoredBursts().Should().HaveCount(bursts);
        Comparisons().Should().Be(bursts);
    }

    [Fact]
    public async Task RunAsync_NeighbourBeforeThePhotoAtTheGap_IsComparedAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", seconds: 120));
        Checked(b, null);
        Similar(a, b);

        await RunAsync();

        StoredBursts().Should().ContainSingle();
    }

    [Theory]
    [InlineData("Canon", "iPhone 15 Pro")]
    [InlineData("Apple", "iPhone 14")]
    public async Task RunAsync_SimilarPhotoFromAnotherCamera_IsNotComparedAsync(string make, string model)
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1, make: make, model: model));
        Similar(a, b);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 2 });
        Comparisons().Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_SimilarPhotoWithoutExifTime_IsNotComparedAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        reader.Read($"{Root}/{b.FilePath}").Returns(new OriginalPhotoFacts(null, null));
        Similar(a, b);

        await RunAsync();

        StoredBursts().Should().BeEmpty();
        Comparisons().Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_SimilarToASoftDeletedPhoto_IsNotComparedAsync()
    {
        var gone = Photo("a.heic", softDeleted: true);
        Checked(gone, null);
        var b = Photo("b.heic", 1);
        Similar(gone, b);

        await RunAsync();

        Comparisons().Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_MatchesChain_AllThreeInOneBurstAsync()
    {
        var (a, b, c) = (Photo("a.heic"), Photo("b.heic", 2), Photo("c.heic", 4));
        Similar(a, b);
        Similar(b, c);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 3, Created = 1, Matched = 2 });
        BurstsOf(a, b, c).Values.Should().AllBeEquivalentTo(StoredBursts().Single().Id);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public async Task RunAsync_ChainReachingTheSpan_JoinsAtItAndStopsBeyondItAsync(int lastSeconds, bool joins)
    {
        var (a, b, c, d) = (Photo("a.heic"), Photo("b.heic", seconds: 100), Photo("c.heic", seconds: 200), Photo("d.heic", seconds: lastSeconds));
        Similar(a, b);
        Similar(b, c);
        Similar(c, d);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 4, Created = 1, Matched = 3 });
        var burst = StoredBursts().Single().Id;
        BurstsOf(a, b, c, d).Should().BeEquivalentTo(new Dictionary<string, Guid?>
        {
            ["a.heic"] = burst,
            ["b.heic"] = burst,
            ["c.heic"] = burst,
            ["d.heic"] = joins ? burst : null,
        });
    }

    [Fact]
    public async Task RunAsync_SpanCountsOnlyActiveMembersAsync()
    {
        var gone = Photo("a.heic", softDeleted: true);
        var (b, c) = (Photo("b.heic", seconds: 120), Photo("c.heic", seconds: 270));
        Checked(b, null);
        Checked(c, null);
        StoredBurst(60, gone, b, c);
        var fresh = Photo("d.heic", seconds: 330);
        Similar(c, fresh);

        await RunAsync();

        BurstsOf(fresh)["d.heic"].Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_SimilarOnlyToABurstItWouldStretchBeyondTheSpan_StaysAloneAsync()
    {
        var (x, n) = (Photo("x.heic"), Photo("n.heic", seconds: 300));
        Checked(x, null);
        Checked(n, null);
        StoredBurst(60, x, n);
        var fresh = Photo("f.heic", seconds: 400);
        Similar(n, fresh);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Matched = 1 });
        StoredBursts().Should().ContainSingle();
        BurstsOf(fresh)["f.heic"].Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_SimilarToTwoNeighbours_JoinsTheOlderFirstAndSkipsWhatThenBreaksTheSpanAsync()
    {
        var (x, older) = (Photo("x.heic", 0), Photo("o.heic", 2));
        Checked(x, null);
        Checked(older, null);
        var stored = StoredBurst(60, x, older);
        var newer = Photo("n.heic", 6);
        Checked(newer, null);
        var fresh = Photo("p.heic", 4);
        Similar(fresh, older);
        Similar(fresh, newer);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Matched = 2 });
        BurstsOf(x, older, fresh, newer).Should().BeEquivalentTo(new Dictionary<string, Guid?>
        {
            ["x.heic"] = stored,
            ["o.heic"] = stored,
            ["p.heic"] = stored,
            ["n.heic"] = null,
        });
    }

    [Fact]
    public async Task RunAsync_SimilarToAFrameOfABurstThatWouldBreakTheSpan_LeavesThatBurstAloneAsync()
    {
        var (x, o, n, y) = (Photo("x.heic"), Photo("o.heic", seconds: 100), Photo("n.heic", seconds: 300), Photo("y.heic", seconds: 500));
        foreach (var photo in new[] { x, o, n, y })
            Checked(photo, null);
        var joined = StoredBurst(60, x, o);
        var skipped = StoredBurst(30, n, y);
        var fresh = Photo("f.heic", seconds: 200);
        Similar(fresh, o);
        Similar(fresh, n);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Matched = 2 });
        StoredBursts().Select(b => b.Id).Should().Equal(joined, skipped);
        BurstsOf(x, o, fresh, n, y).Should().BeEquivalentTo(new Dictionary<string, Guid?>
        {
            ["x.heic"] = joined,
            ["o.heic"] = joined,
            ["f.heic"] = joined,
            ["n.heic"] = skipped,
            ["y.heic"] = skipped,
        });
    }

    [Fact]
    public async Task RunAsync_SimilarToAFrameOfACameraBurst_JoinsThatBurstAsync()
    {
        var (a, b, c) = (Photo("a.heic"), Photo("b.heic", 1), Photo("c.heic", 2));
        Labelled(a, BurstA);
        Labelled(b, BurstA);
        Similar(b, c);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 3, Created = 1, Matched = 1, NotMatched = 1 });
        BurstsOf(a, b, c).Values.Should().AllBeEquivalentTo(StoredBursts().Single().Id);
    }

    [Fact]
    public async Task RunAsync_SimilarToFramesOfTwoBursts_MergesThemIntoTheOlderWithTheEarliestCoverAsync()
    {
        var (a, b, c, d) = (Photo("a.heic", 0), Photo("b.heic", 1), Photo("c.heic", 3), Photo("d.heic", 4));
        foreach (var photo in new[] { a, b, c, d })
            Checked(photo, null);
        StoredBurst(30, c, d);
        var older = StoredBurst(60, a, b);
        var fresh = Photo("e.heic", 2);
        Similar(fresh, b);
        Similar(fresh, c);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Merged = 1, Matched = 2, NotMatched = 2 });
        StoredBursts().Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = older, PrimaryAssetId = a.Id });
        BurstsOf(a, b, c, d, fresh).Values.Should().AllBeEquivalentTo(older);
    }

    [Fact]
    public async Task RunAsync_NeighbourWithTheSameLabel_IsNotComparedAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        Labelled(a, BurstA);
        Labelled(b, BurstA);
        Similar(a, b);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 2, Created = 1 });
        Comparisons().Should().Be(0);
        StoredBursts().Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_NeighbourInTheBurstOfALabelledFrame_IsNotComparedAsync()
    {
        var (a, u) = (Photo("a.heic"), Photo("u.heic", 1));
        Checked(a, BurstA);
        Checked(u, null);
        var stored = StoredBurst(60, a, u);
        var fresh = Photo("f.heic", 2);
        Labelled(fresh, BurstA);
        Similar(fresh, u);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1 });
        Comparisons().Should().Be(0);
        BurstsOf(fresh)["f.heic"].Should().Be(stored);
    }

    [Fact]
    public async Task RunAsync_UnlabelledNeighbourOutsideAnyBurst_IsComparedBesideALabelledLonePhotoAsync()
    {
        var (a, u) = (Photo("a.heic"), Photo("u.heic", 1));
        Checked(a, BurstA);
        Checked(u, null);
        var fresh = Photo("f.heic", 2);
        Labelled(fresh, BurstA);
        Similar(fresh, u);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Created = 1, Matched = 1 });
        Comparisons().Should().Be(1);
        BurstsOf(a, u, fresh).Values.Should().AllBeEquivalentTo(StoredBursts().Single().Id);
    }

    [Fact]
    public async Task RunAsync_CheckedPairs_AreNotComparedAgainAsync()
    {
        Photo("a.heic");
        Photo("b.heic", 1);
        await RunAsync();
        matcher.ClearReceivedCalls();

        await RunAsync();

        Comparisons().Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_LogsEachComparedPairsScoreAtDebugAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        Similar(a, b, 0.4321);

        await RunAsync();

        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Debug).Which.Message.Should().Be(
            $"Burst detection compared photo {b.Id} with {a.Id}: score 0.432.");
    }

    [Fact]
    public async Task RunAsync_ForgetsTheThumbnailsAfterEachBatchAsync()
    {
        foreach (var i in Enumerable.Range(0, BurstDetectionHandler.BatchSize + 1))
            Photo($"{i}.heic", i * 10);

        await RunAsync();

        matcher.Received(2).Forget();
    }

    // ==========================================================================
    // The checked mark and incremental runs
    // ==========================================================================

    [Fact]
    public async Task RunAsync_MarksEachPhotoOnceAndASecondRunChecksNothingAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        Labelled(a, BurstA);
        Labelled(b, BurstA);
        await RunAsync();
        reader.ClearReceivedCalls();

        var second = await RunAsync();

        second.Should().Be(new BurstDetectionResult());
        reader.DidNotReceiveWithAnyArgs().Read(default!);
        StoredChecks().Should().BeEquivalentTo(
            [
                new { AssetId = a.Id, IsCandidate = true, CameraBurstId = BurstA, CheckedAt = Now },
                new { AssetId = b.Id, IsCandidate = true, CameraBurstId = BurstA, CheckedAt = Now },
            ]);
        StoredBursts().Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_NewPhotoWithTheLabelOfAStoredBurst_JoinsItAndBecomesItsCoverIfEarlierAsync()
    {
        var (a, b) = (Photo("a.heic", 5), Photo("b.heic", 6));
        Checked(a, BurstA);
        Checked(b, BurstA);
        var stored = StoredBurst(60, a, b);
        var earlier = Photo("c.heic", 4);
        Labelled(earlier, BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1 });
        BurstsOf(a, b, earlier).Values.Should().AllBeEquivalentTo(stored);
        StoredBursts().Should().ContainSingle().Which.PrimaryAssetId.Should().Be(earlier.Id);
        reader.DidNotReceive().Read($"{Root}/{a.FilePath}");
    }

    [Fact]
    public async Task RunAsync_NewPhotoWithTheLabelOfACheckedLonePhoto_FormsABurstWithItAsync()
    {
        var lone = Photo("a.heic", 1);
        Checked(lone, BurstA);
        var fresh = Photo("b.heic", 0);
        Labelled(fresh, BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Created = 1 });
        var burst = StoredBursts().Should().ContainSingle().Which;
        burst.PrimaryAssetId.Should().Be(fresh.Id);
        BurstsOf(lone, fresh).Values.Should().AllBeEquivalentTo(burst.Id);
    }

    [Fact]
    public async Task RunAsync_NewPhotoJoiningTwoStoredBursts_MergesThemIntoTheOlderAsync()
    {
        var (a, b, c, d) = (Photo("a.heic", 4), Photo("b.heic", 5), Photo("c.heic", 2), Photo("d.heic", 3));
        Checked(a, BurstA);
        Checked(b, BurstA);
        Checked(c, BurstA);
        Checked(d, BurstA);
        var older = StoredBurst(60, a, b);
        StoredBurst(30, c, d);
        var fresh = Photo("e.heic", 9);
        Labelled(fresh, BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Merged = 1 });
        StoredBursts().Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = older, PrimaryAssetId = c.Id });
        BurstsOf(a, b, c, d, fresh).Values.Should().AllBeEquivalentTo(older);
    }

    [Fact]
    public async Task RunAsync_ProcessesPhotosOldestFirstAndReportsEachAsync()
    {
        var (late, early) = (Photo("a.heic", 9), Photo("b.heic", 1));

        await RunAsync();

        Received.InOrder(() =>
        {
            reader.Read($"{Root}/{early.FilePath}");
            progress.Report(1, 2, "Checking photos for bursts");
            reader.Read($"{Root}/{late.FilePath}");
            progress.Report(2, 2, "Checking photos for bursts");
        });
    }

    [Fact]
    public async Task RunAsync_SameCaptureTime_ProcessesTheLowerIdFirstAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic"));
        var (first, second) = a.Id.CompareTo(b.Id) < 0 ? (a, b) : (b, a);

        await RunAsync();

        Received.InOrder(() =>
        {
            reader.Read($"{Root}/{first.FilePath}");
            reader.Read($"{Root}/{second.FilePath}");
        });
    }

    [Fact]
    public async Task RunAsync_JoiningTwoBurstsCreatedAtOnce_MergesIntoTheLowerIdAsync()
    {
        var (a, b, c, d) = (Photo("a.heic", 1), Photo("b.heic", 2), Photo("c.heic", 3), Photo("d.heic", 4));
        foreach (var photo in new[] { a, b, c, d })
            Checked(photo, BurstA);
        var (one, two) = (StoredBurst(60, a, b), StoredBurst(60, c, d));
        var fresh = Photo("e.heic", 9);
        Labelled(fresh, BurstA);

        await RunAsync();

        StoredBursts().Should().ContainSingle().Which.Id.Should().Be(one.CompareTo(two) < 0 ? one : two);
    }

    [Fact]
    public async Task RunAsync_SameCaptureTime_CoverIsTheLowerIdAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic"));
        Labelled(a, BurstA);
        Labelled(b, BurstA);
        var lower = a.Id.CompareTo(b.Id) < 0 ? a.Id : b.Id;

        await RunAsync();

        StoredBursts().Single().PrimaryAssetId.Should().Be(lower);
    }

    [Fact]
    public async Task RunAsync_MoreThanOneBatch_ChecksEveryPhotoAndKeepsNoneTrackedAsync()
    {
        var photos = Enumerable.Range(0, BurstDetectionHandler.BatchSize + 1).Select(i => Photo($"{i}.heic", i)).ToList();
        Labelled(photos[0], BurstA);
        Labelled(photos[^1], BurstA);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = BurstDetectionHandler.BatchSize + 1, Created = 1, NotMatched = 199 });
        StoredChecks().Should().HaveCount(BurstDetectionHandler.BatchSize + 1);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_NextBatch_StartsAfterTheLastPhotoOfThePreviousOneAsync()
    {
        var photo = Photo("a.heic");
        IReadOnlyList<MediaAsset> none = [];
        var bursts = Substitute.For<IBurstRepository>();
        bursts.GetInConfigWithMembersAsync(ConfigId, Arg.Any<CancellationToken>()).Returns([]);
        bursts.GetUncheckedPhotosAsync(ConfigId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([photo], none);
        bursts.GetUncheckedPhotosAsync(ConfigId, photo, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(none);

        await new BurstDetectionHandler(
                new ActiveStorageConfigs(new EfUserStorageConfigRepository(db), activeLogger),
                bursts, Substitute.For<IUnitOfWork>(), reader, matcher, fileSystem, clock, guids,
                Options.Create(new WorkerSettings()), logger)
            .RunAsync(new BurstDetectionPayload(ConfigId), progress, Ct);

        await bursts.Received(1).GetUncheckedPhotosAsync(ConfigId, photo, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Cancelled_StopsBeforeSavingThePhotoBeingCheckedAsync()
    {
        var (a, b) = (Photo("a.heic"), Photo("b.heic", 1));
        using var cancel = new CancellationTokenSource();
        reader.Read($"{Root}/{a.FilePath}").Returns(_ =>
        {
            cancel.Cancel();
            return new OriginalPhotoFacts(Noon, null);
        });

        var act = () => RunForAsync(ConfigId, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        StoredChecks().Should().BeEmpty();
        reader.DidNotReceive().Read($"{Root}/{b.FilePath}");
    }

    [Fact]
    public async Task RunAsync_LogsTheSummaryAsync()
    {
        var (a, b, c) = (Photo("a.heic"), Photo("b.heic", 1), Photo("c.heic", 2));
        Photo("d.heic", 3);
        Labelled(a, BurstA);
        Labelled(b, BurstA);
        Similar(b, c);

        await RunAsync();

        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Which.Message.Should().Be(
            $"Burst detection for storage config {ConfigId} checked 4 photo(s): 1 burst(s) created, 0 merged, 0 dissolved; 1 compared pair(s) matched, 3 did not.");
    }

    // ==========================================================================
    // Repair
    // ==========================================================================

    [Fact]
    public async Task RunAsync_BurstLeftWithOneActiveMember_IsDissolvedAsync()
    {
        var (a, gone) = (Photo("a.heic"), Photo("b.heic", 1, softDeleted: true));
        StoredBurst(60, a, gone);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Dissolved = 1 });
        StoredBursts().Should().BeEmpty();
        BurstsOf(a, gone).Values.Should().AllSatisfy(id => id.Should().BeNull());
    }

    [Fact]
    public async Task RunAsync_NothingLeftToCheck_StillSavesTheRepairAsync()
    {
        var (a, gone) = (Photo("a.heic"), Photo("b.heic", 1, softDeleted: true));
        Checked(a, null);
        StoredBurst(60, a, gone);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Dissolved = 1 });
        StoredBursts().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_BurstsWithoutMembers_AreDeletedInOneStatementAndCountAsDissolvedAsync()
    {
        var (a, gone) = (Photo("a.heic"), Photo("b.heic", 1, softDeleted: true));
        StoredBurst(60, a, gone);
        var memberless = StoredBurst(30);
        repository.Memberless = 2;

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 1, Dissolved = 3 });
        repository.MemberlessDeletions.Should().Be(1);
        StoredBursts().Should().ContainSingle().Which.Id.Should().Be(memberless);
    }

    [Fact]
    public async Task RunAsync_CoverSoftDeleted_EarliestActiveMemberBecomesCoverAsync()
    {
        var (cover, b, c) = (Photo("a.heic", 0, softDeleted: true), Photo("b.heic", 7), Photo("c.heic", 5));
        var id = StoredBurst(60, cover, b, c);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult { Checked = 2, NotMatched = 1 });
        StoredBursts().Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = id, PrimaryAssetId = c.Id });
    }

    [Fact]
    public async Task RunAsync_CoverlessBurst_GetsItsEarliestMemberAsCoverAsync()
    {
        var (a, b) = (Photo("a.heic", 3), Photo("b.heic", 2));
        ClearCover(StoredBurst(60, a, b));

        await RunAsync();

        StoredBursts().Single().PrimaryAssetId.Should().Be(b.Id);
    }

    [Fact]
    public async Task RunAsync_AnotherConfigsBurst_IsLeftAloneAsync()
    {
        var (a, gone) = (Photo("a.heic", config: OtherConfigId), Photo("b.heic", 1, config: OtherConfigId, softDeleted: true));
        StoredBurst(60, a, gone);

        var result = await RunAsync();

        result.Should().Be(new BurstDetectionResult());
        StoredBursts().Should().ContainSingle();
    }

    private sealed class ScriptedBursts(EfBurstRepository inner) : IBurstRepository
    {
        public int Memberless { get; set; }

        public int MemberlessDeletions { get; private set; }

        public Task<int> DeleteMemberlessAsync(CancellationToken ct)
        {
            MemberlessDeletions++;
            return Task.FromResult(Memberless);
        }

        public Task<IReadOnlyList<Burst>> GetInConfigWithMembersAsync(Guid storageConfigId, CancellationToken ct)
            => inner.GetInConfigWithMembersAsync(storageConfigId, ct);

        public int BurstLoads { get; private set; }

        public Task<IReadOnlyList<Burst>> GetWithMembersAsync(IReadOnlyCollection<Guid> burstIds, CancellationToken ct)
        {
            BurstLoads++;
            return inner.GetWithMembersAsync(burstIds, ct);
        }

        public Task<int> CountUncheckedPhotosAsync(Guid storageConfigId, CancellationToken ct)
            => inner.CountUncheckedPhotosAsync(storageConfigId, ct);

        public Task<IReadOnlyList<MediaAsset>> GetUncheckedPhotosAsync(
            Guid storageConfigId, MediaAsset? after, int count, CancellationToken ct)
            => inner.GetUncheckedPhotosAsync(storageConfigId, after, count, ct);

        public Task<IReadOnlyList<MediaAsset>> GetCandidatesLabelledAsync(Guid storageConfigId, string cameraBurstId, CancellationToken ct)
            => inner.GetCandidatesLabelledAsync(storageConfigId, cameraBurstId, ct);

        public Task<IReadOnlyList<MediaAsset>> GetCandidatesNearAsync(
            Guid storageConfigId, string cameraMake, string cameraModel, LocalDateTime from, LocalDateTime to, CancellationToken ct)
            => inner.GetCandidatesNearAsync(storageConfigId, cameraMake, cameraModel, from, to, ct);

        public void Add(Burst burst) => inner.Add(burst);

        public void Remove(Burst burst) => inner.Remove(burst);

        public void AddCheck(BurstCheck check) => inner.AddCheck(check);

        public Task RemoveCheckAsync(Guid assetId, CancellationToken ct) => inner.RemoveCheckAsync(assetId, ct);

        public void ForgetLoaded() => inner.ForgetLoaded();
    }
}
