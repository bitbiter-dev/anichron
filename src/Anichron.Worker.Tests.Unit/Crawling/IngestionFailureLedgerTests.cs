using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class IngestionFailureLedgerTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 11, 1, 0, 0);
    private static readonly DateTime Written = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string database = Guid.NewGuid().ToString();
    private readonly MockFileSystem fileSystem = new();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly UserStorageConfig config = new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" };

    public IngestionFailureLedgerTests() => clock.GetCurrentInstant().Returns(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SingleFileItem Item(string path) => new($"/nas/{path}", path, MediaType.Image);

    private static string FingerprintOf(int length) => $"{length}:{Written.Ticks}";

    private void AddFile(string path, int length = 1)
        => fileSystem.AddFile($"/nas/{path}", new MockFileData(new byte[length]) { LastWriteTime = Written });

    private void Now(Instant now) => clock.GetCurrentInstant().Returns(now);

    private IngestionFailureLedger Ledger(AnichronDbContextLease lease, WorkerSettings? settings = null)
        => new(new EfIngestionFailureRepository(lease.Db), lease.Db, fileSystem, clock, Options.Create(settings ?? new WorkerSettings()));

    private sealed class AnichronDbContextLease(string database) : IAsyncDisposable
    {
        public AnichronDbContext Db { get; } = AnichronDbContexts.InMemory(database);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static CrawlRun Run(
        IEnumerable<string>? reached = null,
        IEnumerable<string>? succeeded = null,
        IEnumerable<FailedItem>? failures = null,
        IEnumerable<string>? found = null)
    {
        var reachedPaths = (reached ?? []).ToHashSet(StringComparer.Ordinal);
        return new CrawlRun(new CrawlResult(0, 0, 0), 0)
        {
            ReachedPaths = reachedPaths,
            FoundPaths = (found ?? reachedPaths).ToHashSet(StringComparer.Ordinal),
            SucceededPaths = (succeeded ?? []).ToHashSet(StringComparer.Ordinal),
            Failures = [.. failures ?? []],
        };
    }

    private static readonly MissingFiles Swept = new(0, null);

    private async Task RecordAsync(CrawlRun run, MissingFiles? missing = null, WorkerSettings? settings = null)
    {
        await using var lease = new AnichronDbContextLease(database);
        await Ledger(lease, settings).RecordAsync(config, run, missing ?? Swept, Ct);
    }

    private Task FailAsync(string path, string reason = "IOException: locked", WorkerSettings? settings = null)
        => RecordAsync(
            Run(reached: [path], failures: [new FailedItem(path, FingerprintOf(1), reason)]), settings: settings);

    private async Task<List<IngestionFailure>> RowsAsync()
    {
        await using var lease = new AnichronDbContextLease(database);
        return await lease.Db.IngestionFailures.OrderBy(row => row.FilePath).ToListAsync(Ct);
    }

    private async Task<FailingFiles> LoadAsync()
    {
        await using var lease = new AnichronDbContextLease(database);
        return await Ledger(lease).LoadAsync(config, Ct);
    }

    private async Task SeedAsync(IngestionFailure row)
    {
        await using var lease = new AnichronDbContextLease(database);
        lease.Db.IngestionFailures.Add(row);
        await lease.Db.SaveChangesAsync(Ct);
    }

    private IngestionFailure Row(string path, Guid? configId = null, int failures = 1, string? fingerprint = null) => new()
    {
        StorageConfigId = configId ?? config.Id,
        FilePath = path,
        Fingerprint = fingerprint ?? FingerprintOf(1),
        Failures = failures,
        Reason = "IOException: locked",
        FirstFailedAt = Start - Duration.FromDays(3),
        LastFailedAt = Start - Duration.FromDays(1),
        RetryAfter = Start + Duration.FromHours(2),
    };

    // ==========================================================================
    // Recording a failure
    // ==========================================================================

    [Fact]
    public async Task RecordAsync_FirstFailure_AddsARowDueAgainAtTheNextCrawlAsync()
    {
        await FailAsync("a.jpg");

        (await RowsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            StorageConfigId = config.Id,
            FilePath = "a.jpg",
            Fingerprint = FingerprintOf(1),
            Failures = 1,
            Reason = "IOException: locked",
            FirstFailedAt = Start,
            LastFailedAt = Start,
            RetryAfter = Start + Duration.FromHours(2),
        });
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 6)]
    [InlineData(3, 14)]
    [InlineData(4, 30)]
    public async Task RecordAsync_NthFailureInARow_IsDueAgainAfterTheDoublingDelayAtAFourHourIntervalAsync(int failures, int hours)
    {
        for (var crawl = 0; crawl < failures; crawl++)
        {
            Now(Start + Duration.FromDays(crawl));
            await FailAsync("a.jpg");
        }

        var last = Start + Duration.FromDays(failures - 1);
        (await RowsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Failures = failures,
            FirstFailedAt = Start,
            LastFailedAt = last,
            RetryAfter = last + Duration.FromHours(hours),
        });
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 7)]
    public async Task LoadAsync_AfterTheNthFailure_SkipsThatManyCrawlsDueAnIntervalAfterTheLastFinishedAsync(int failures, int skipped)
    {
        AddFile("a.jpg");
        for (var crawl = 1; crawl <= failures; crawl++)
        {
            Now(Start + Duration.FromDays(crawl));
            await FailAsync("a.jpg");
        }

        var last = Start + Duration.FromDays(failures);
        var skips = 0;
        for (var crawl = 1; crawl <= 10; crawl++)
        {
            Now(last + Duration.FromHours(4 * crawl));
            if ((await LoadAsync()).Skipping(Item("a.jpg")) is null)
                break;
            skips++;
        }

        skips.Should().Be(skipped);
    }

    [Fact]
    public async Task RecordAsync_DelayReachesTheCap_IsDueAgainAfterTheCapAsync()
    {
        await SeedAsync(Row("a.jpg", failures: int.MaxValue - 1));

        await FailAsync("a.jpg");

        (await RowsAsync()).Single().RetryAfter.Should().Be(Start + Duration.FromDays(7));
    }

    [Fact]
    public async Task RecordAsync_UsesTheConfiguredCapAndIntervalAsync()
    {
        await SeedAsync(Row("a.jpg", failures: 3));
        var settings = new WorkerSettings { CrawlIntervalHours = 2, IngestionRetryMaxDays = 0.5 };

        await FailAsync("a.jpg", settings: settings);

        (await RowsAsync()).Single().RetryAfter.Should().Be(Start + Duration.FromHours(12));
    }

    [Fact]
    public async Task RecordAsync_ShortDelayUnderTheConfiguredInterval_IsNotCappedAsync()
    {
        await SeedAsync(Row("a.jpg", failures: 1));

        await FailAsync("a.jpg", settings: new WorkerSettings { CrawlIntervalHours = 2, IngestionRetryMaxDays = 0.5 });

        (await RowsAsync()).Single().RetryAfter.Should().Be(Start + Duration.FromHours(3));
    }

    [Fact]
    public async Task RecordAsync_FailureAfterTheFilesChanged_StartsCountingAgainFromNowAsync()
    {
        await SeedAsync(Row("a.jpg", failures: 5, fingerprint: FingerprintOf(9)));

        await FailAsync("a.jpg", "UnauthorizedAccessException: denied");

        (await RowsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Fingerprint = FingerprintOf(1),
            Failures = 1,
            Reason = "UnauthorizedAccessException: denied",
            FirstFailedAt = Start,
            RetryAfter = Start + Duration.FromHours(2),
        });
    }

    [Fact]
    public async Task RecordAsync_LongReason_IsTruncatedToTheColumnAsync()
    {
        await FailAsync("a.jpg", new string('x', 501));

        (await RowsAsync()).Single().Reason.Should().Be(new string('x', 500));
    }

    // ==========================================================================
    // What a crawl clears
    // ==========================================================================

    [Fact]
    public async Task RecordAsync_ItemSucceeded_ClearsItsRowAsync()
    {
        await SeedAsync(Row("a.jpg"));
        await SeedAsync(Row("b.jpg"));

        await RecordAsync(Run(reached: ["a.jpg", "b.jpg"], succeeded: ["a.jpg"]), new MissingFiles(0, MissingFileSkip.IncompleteWalk));

        (await RowsAsync()).Select(row => row.FilePath).Should().Equal("b.jpg");
    }

    [Fact]
    public async Task RecordAsync_CompleteWalk_PrunesRowsOfFilesItDidNotReachButKeepsSkippedOnesAsync()
    {
        await SeedAsync(Row("gone.jpg"));
        await SeedAsync(Row("skipped.jpg"));

        await RecordAsync(Run(reached: ["skipped.jpg", "other.jpg"], succeeded: ["other.jpg"]));

        (await RowsAsync()).Select(row => row.FilePath).Should().Equal("skipped.jpg");
    }

    [Fact]
    public async Task RecordAsync_PathNowALivePhotosVideo_IsPrunedAsync()
    {
        await SeedAsync(Row("IMG_0001.MOV"));

        await RecordAsync(Run(reached: ["IMG_0001.HEIC"], succeeded: ["IMG_0001.HEIC"], found: ["IMG_0001.HEIC", "IMG_0001.MOV"]));

        (await RowsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(MissingFileSkip.IncompleteWalk)]
    [InlineData(MissingFileSkip.EmptyRoot)]
    public async Task RecordAsync_SweepSkipped_KeepsRowsOfFilesNotReachedAsync(MissingFileSkip reason)
    {
        await SeedAsync(Row("gone.jpg"));

        await RecordAsync(Run(reached: ["other.jpg"], failures: [new FailedItem("other.jpg", FingerprintOf(1), "IOException: locked")]),
            new MissingFiles(0, reason));

        (await RowsAsync()).Select(row => row.FilePath).Should().Equal("gone.jpg", "other.jpg");
    }

    [Fact]
    public async Task RecordAsync_WalkReachedNothing_KeepsEveryRowAsync()
    {
        await SeedAsync(Row("a.jpg"));

        await RecordAsync(Run());

        (await RowsAsync()).Select(row => row.FilePath).Should().Equal("a.jpg");
    }

    [Fact]
    public async Task RecordAsync_LeavesOtherConfigsRowsAloneAsync()
    {
        var other = Guid.NewGuid();
        await SeedAsync(Row("a.jpg", configId: other));

        await RecordAsync(Run(reached: ["b.jpg"], succeeded: ["b.jpg"]));

        (await RowsAsync()).Should().ContainSingle().Which.StorageConfigId.Should().Be(other);
    }

    // ==========================================================================
    // Skipping
    // ==========================================================================

    [Fact]
    public async Task Skipping_RowNotDueAndFilesUnchanged_ReturnsTheRowAsync()
    {
        AddFile("a.jpg");
        await SeedAsync(Row("a.jpg"));

        (await LoadAsync()).Skipping(Item("a.jpg")).Should().BeEquivalentTo(new { FilePath = "a.jpg", Failures = 1 });
    }

    [Fact]
    public async Task Skipping_RowDue_ReturnsNullAsync()
    {
        AddFile("a.jpg");
        await SeedAsync(Row("a.jpg"));
        Now(Start + Duration.FromHours(2));

        (await LoadAsync()).Skipping(Item("a.jpg")).Should().BeNull();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task Skipping_FileChangedSinceItFailed_ReturnsNullAsync(int length, bool touched)
    {
        fileSystem.AddFile("/nas/a.jpg", new MockFileData(new byte[length])
        {
            LastWriteTime = touched ? Written.AddSeconds(1) : Written,
        });
        await SeedAsync(Row("a.jpg"));

        (await LoadAsync()).Skipping(Item("a.jpg")).Should().BeNull();
    }

    [Fact]
    public async Task Skipping_OtherPathOrOtherConfig_ReturnsNullAsync()
    {
        AddFile("a.jpg");
        AddFile("b.jpg");
        await SeedAsync(Row("a.jpg", configId: Guid.NewGuid()));
        await SeedAsync(Row("c.jpg"));

        var failing = await LoadAsync();

        (failing.Skipping(Item("a.jpg")), failing.Skipping(Item("b.jpg"))).Should().Be((null, null));
    }

    [Fact]
    public async Task Skipping_LivePhotoWhoseVideoChanged_ReturnsNullAsync()
    {
        AddFile("live.heic");
        AddFile("live.mov", length: 2);
        var pair = new LivePhotoPairItem("/nas/live.heic", "live.heic", "/nas/live.mov", "live.mov");
        await SeedAsync(Row("live.heic", fingerprint: $"{FingerprintOf(1)}|{FingerprintOf(2)}"));
        var before = (await LoadAsync()).Skipping(pair);
        AddFile("live.mov", length: 3);

        var after = (await LoadAsync()).Skipping(pair);

        (before is null, after is null).Should().Be((false, true));
    }

    [Fact]
    public void Fingerprint_MissingFile_IsADashThatNoStatMatches()
        => new FailingFiles(new Dictionary<string, IngestionFailure>(), fileSystem, clock).Fingerprint(Item("gone.jpg"))
            .Should().Be("-");
}
