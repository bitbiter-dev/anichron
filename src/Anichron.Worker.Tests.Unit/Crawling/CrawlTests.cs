using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class CrawlTests
{
    private static readonly Guid MiraId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly IUserStorageConfigRepository configs = Substitute.For<IUserStorageConfigRepository>();
    private readonly IFileIngestionPipeline pipeline = Substitute.For<IFileIngestionPipeline>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly ISharedContentReporter reporter = Substitute.For<ISharedContentReporter>();
    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly IMissingFileSweep sweep = Substitute.For<IMissingFileSweep>();
    private readonly FakeLogger<CrawlHandler> logger = new();
    private readonly FakeLogger<ActiveStorageConfigs> activeLogger = new();

    public CrawlTests()
        => sweep.SweepAsync(Arg.Any<UserStorageConfig>(), Arg.Any<CrawlRun>(), Arg.Any<CancellationToken>())
            .Returns(new MissingFiles(0, null));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserStorageConfig Config(bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        UserId = MiraId,
        RootPath = "/data/originals/mira",
        IsActive = active,
    };

    private Task<object?> RunAsync(Guid configId)
        => new CrawlHandler(new ActiveStorageConfigs(configs, activeLogger), configs, pipeline, sweep, reporter, queue, logger).RunAsync(new CrawlPayload(configId), progress, Ct);

    // ==========================================================================
    // CrawlHandler
    // ==========================================================================

    [Fact]
    public async Task RunAsync_ActiveConfig_CrawlsItAndReturnsTheCountsWithItsDuplicateReportAsync()
    {
        var config = Config();
        var other = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        configs.GetActiveByUserIdAsync(MiraId, Ct).Returns([config, other]);
        var run = new CrawlRun(new CrawlResult(5, 4, 1), DuplicateCopies: 2) { SoftDeleted = 1, AutoRestored = 3 };
        pipeline.RunAsync(config, progress, Ct).Returns(run);
        sweep.SweepAsync(config, run, Ct).Returns(new MissingFiles(SoftDeleted: 4, SkippedBecause: null));
        reporter.ReportAsync(config, Arg.Is<IReadOnlyCollection<UserStorageConfig>>(c => c.SequenceEqual(new[] { config, other })), 2, Ct)
            .Returns(new DuplicateCounts(2, 1));

        var result = await RunAsync(config.Id);

        result.Should().Be(new CrawlResult(5, 4, 1)
        {
            Duplicates = new DuplicateCounts(2, 1),
            Reconciliation = new ReconciliationCounts(SoftDeleted: 5, AutoRestored: 3, SkippedBecause: null),
        });
        await queue.Received(1).EnqueueAsync(new BurstDetectionPayload(config.Id), null, Ct);
        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Information && e.Message.Contains("/data/originals/mira"));
    }

    [Fact]
    public async Task RunAsync_SweepsBeforeTheDuplicateReportAndTheBurstDetectionAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        var steps = new List<string>();
        pipeline.RunAsync(config, progress, Ct).Returns(new CrawlRun(new CrawlResult(1, 1, 0), 0));
        sweep.SweepAsync(config, Arg.Any<CrawlRun>(), Ct).Returns(_ =>
        {
            steps.Add("sweep");
            return new MissingFiles(0, null);
        });
        reporter.ReportAsync(Arg.Any<UserStorageConfig>(), Arg.Any<IReadOnlyCollection<UserStorageConfig>>(), Arg.Any<int>(), Ct)
            .Returns(_ =>
            {
                steps.Add("report");
                return new DuplicateCounts(0, 0);
            });
        queue.When(q => q.EnqueueAsync(Arg.Any<BurstDetectionPayload>(), null, Ct)).Do(_ => steps.Add("enqueue"));

        await RunAsync(config.Id);

        steps.Should().Equal("sweep", "report", "enqueue");
    }

    [Fact]
    public async Task RunAsync_LogsOneSummaryOfTheReconciliationAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        var run = new CrawlRun(new CrawlResult(3, 3, 0), 0) { SoftDeleted = 1, AutoRestored = 2 };
        pipeline.RunAsync(config, progress, Ct).Returns(run);
        sweep.SweepAsync(config, run, Ct).Returns(new MissingFiles(3, null));

        await RunAsync(config.Id);

        logger.Collector.GetSnapshot().Where(e => e.Message.StartsWith("Reconciled", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Match<FakeLogRecord>(e => e.Level == LogLevel.Information
                && e.Message == $"Reconciled storage config {config.Id}: 4 soft-deleted, 2 auto-restored.");
    }

    [Fact]
    public async Task RunAsync_SweepSkipped_ReportsAndLogsWhyAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        var run = new CrawlRun(new CrawlResult(0, 0, 0), 0) { AutoRestored = 1 };
        pipeline.RunAsync(config, progress, Ct).Returns(run);
        sweep.SweepAsync(config, run, Ct).Returns(new MissingFiles(0, MissingFileSkip.EmptyRoot));

        var result = await RunAsync(config.Id);

        result.Should().BeOfType<CrawlResult>().Which.Reconciliation
            .Should().Be(new ReconciliationCounts(0, 1, MissingFileSkip.EmptyRoot));
        logger.Collector.GetSnapshot().Where(e => e.Message.StartsWith("Reconciled", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Match<FakeLogRecord>(e => e.Level == LogLevel.Information
                && e.Message == $"Reconciled storage config {config.Id}: 0 soft-deleted, 1 auto-restored; "
                    + "missing files were not looked for (EmptyRoot).");
    }

    [Fact]
    public void Result_NamesTheSkipReasonInItsJson()
        => WorkJson.SerializeResult(new CrawlResult(0, 0, 0) { Reconciliation = new ReconciliationCounts(0, 0, MissingFileSkip.IncompleteWalk) })
            .Should().Contain("""
                "reconciliation":{"softDeleted":0,"autoRestored":0,"skippedBecause":"IncompleteWalk"}
                """);

    [Fact]
    public async Task RunAsync_DuplicateReportFails_LogsItAndStillSucceedsWithoutTheReportAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct).Returns(new CrawlRun(new CrawlResult(1, 1, 0), 0));
        reporter.ReportAsync(Arg.Any<UserStorageConfig>(), Arg.Any<IReadOnlyCollection<UserStorageConfig>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<DuplicateCounts>(_ => throw new InvalidOperationException("db down"));

        var result = await RunAsync(config.Id);

        result.Should().BeOfType<CrawlResult>().Which.Duplicates.Should().BeNull();
        await queue.Received(1).EnqueueAsync(new BurstDetectionPayload(config.Id), null, Ct);
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Should().Match<FakeLogRecord>(e =>
                e.Message == $"Reporting duplicate content for storage config {config.Id} failed."
                && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task RunAsync_ShutdownDuringTheDuplicateReport_PropagatesAsync()
    {
        var config = Config();
        using var shutdown = new CancellationTokenSource();
        configs.FindByIdAsync(config.Id, shutdown.Token).Returns(config);
        pipeline.RunAsync(config, progress, shutdown.Token).Returns(new CrawlRun(new CrawlResult(1, 1, 0), 0));
        reporter.ReportAsync(Arg.Any<UserStorageConfig>(), Arg.Any<IReadOnlyCollection<UserStorageConfig>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<DuplicateCounts>(_ =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        });

        var act = () => new CrawlHandler(new ActiveStorageConfigs(configs, activeLogger), configs, pipeline, sweep, reporter, queue, logger)
            .RunAsync(new CrawlPayload(config.Id), progress, shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Collector.GetSnapshot().Should().NotContain(e => e.Level == LogLevel.Error);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
    }

    [Fact]
    public async Task RunAsync_InactiveConfig_SkipsWithoutCrawlingAsync()
    {
        var config = Config(active: false);
        configs.FindByIdAsync(config.Id, Ct).Returns(config);

        var result = await RunAsync(config.Id);

        result.Should().Be(new StorageConfigSkipped("The storage config is missing or inactive."));
        await pipeline.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, Ct);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
        activeLogger.Collector.GetSnapshot().Should().ContainSingle().Which.Message.Should().Be(
            $"Skipping Crawl of storage config {config.Id}: it is missing or inactive.");
    }

    [Fact]
    public async Task RunAsync_ConfigGone_SkipsWithoutCrawlingAsync()
    {
        var result = await RunAsync(Guid.NewGuid());

        result.Should().BeOfType<StorageConfigSkipped>();
        await pipeline.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task RunAsync_RootMissing_FailsOnlyThisItemByThrowingAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct)
            .Returns<CrawlRun>(_ => throw new DirectoryNotFoundException("/data/originals/mira"));

        var act = () => RunAsync(config.Id);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        await reporter.DidNotReceiveWithAnyArgs().ReportAsync(default!, default!, default, Ct);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
    }

    [Fact]
    public async Task RunAsync_ShutdownMidCrawl_PropagatesTheCancellationAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct).Returns<CrawlRun>(_ => throw new OperationCanceledException());

        var act = () => RunAsync(config.Id);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
    }

    // ==========================================================================
    // Duplicate copies, through the real pipeline and reporter
    // ==========================================================================

    [Fact]
    public async Task RunAsync_CrawlCancelledAfterCountingACopy_AddsNothingToTheNextCrawlsCopiesAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
        configs.GetActiveByUserIdAsync(MiraId, Arg.Any<CancellationToken>()).Returns([config]);
        var fileSystem = new MockFileSystem();
        fileSystem.AddEmptyFile("/data/originals/mira/a.jpg");
        fileSystem.AddEmptyFile("/data/originals/mira/b.jpg");
        using var cancel = new CancellationTokenSource();
        var middleware = new ScriptedClassificationMiddleware(
            new ScriptedItem(FileOutcome.DuplicateCopy),
            new ScriptedItem(FileOutcome.Unchanged, Cancels: cancel),
            new ScriptedItem(FileOutcome.DuplicateCopy),
            new ScriptedItem(FileOutcome.Unchanged));
        var realPipeline = FileIngestionPipelines.Build([middleware], fileSystem, new WorkerSettings { MaxConcurrentFiles = 1 });
        var handler = new CrawlHandler(
            new ActiveStorageConfigs(configs, activeLogger), configs, realPipeline, sweep,
            new SharedContentReporter(Substitute.For<IMediaAssetRepository>(), new FakeLogger<SharedContentReporter>()),
            queue, logger);

        var cancelled = () => handler.RunAsync(new CrawlPayload(config.Id), progress, cancel.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var next = await handler.RunAsync(new CrawlPayload(config.Id), progress, Ct);

        next.Should().BeOfType<CrawlResult>().Which.Duplicates.Should().Be(new DuplicateCounts(1, 0));
    }
}
