using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Crawling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class CrawlTests
{
    private static readonly Guid MiraId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly IUserStorageConfigRepository configs = Substitute.For<IUserStorageConfigRepository>();
    private readonly IFileIngestionPipeline pipeline = Substitute.For<IFileIngestionPipeline>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly ISharedContentReporter reporter = Substitute.For<ISharedContentReporter>();
    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly FakeLogger<CrawlHandler> logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserStorageConfig Config(bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        UserId = MiraId,
        RootPath = "/data/originals/mira",
        IsActive = active,
    };

    private Task<object?> RunAsync(Guid configId)
        => new CrawlHandler(configs, pipeline, reporter, queue, logger).RunAsync(new CrawlPayload(configId), progress, Ct);

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
        pipeline.RunAsync(config, progress, Ct).Returns(new CrawlResult(5, 4, 1));
        reporter.ReportAsync(config, Arg.Is<IReadOnlyCollection<UserStorageConfig>>(c => c.SequenceEqual(new[] { config, other })), Ct)
            .Returns(new DuplicateCounts(2, 1));

        var result = await RunAsync(config.Id);

        result.Should().Be(new CrawlResult(5, 4, 1) { Duplicates = new DuplicateCounts(2, 1) });
        await queue.Received(1).EnqueueAsync(new BurstDetectionPayload(config.Id), null, Ct);
        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Information && e.Message.Contains("/data/originals/mira"));
    }

    [Fact]
    public async Task RunAsync_DuplicateReportFails_LogsItAndStillSucceedsWithoutTheReportAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct).Returns(new CrawlResult(1, 1, 0));
        reporter.ReportAsync(Arg.Any<UserStorageConfig>(), Arg.Any<IReadOnlyCollection<UserStorageConfig>>(), Arg.Any<CancellationToken>())
            .Returns<DuplicateCounts>(_ => throw new InvalidOperationException("db down"));

        var result = await RunAsync(config.Id);

        result.Should().Be(new CrawlResult(1, 1, 0) { Duplicates = null });
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
        pipeline.RunAsync(config, progress, shutdown.Token).Returns(new CrawlResult(1, 1, 0));
        reporter.ReportAsync(Arg.Any<UserStorageConfig>(), Arg.Any<IReadOnlyCollection<UserStorageConfig>>(), Arg.Any<CancellationToken>())
            .Returns<DuplicateCounts>(_ =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        });

        var act = () => new CrawlHandler(configs, pipeline, reporter, queue, logger)
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

        result.Should().Be(new CrawlSkipped("The storage config is missing or inactive."));
        await pipeline.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, Ct);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Message.Contains(config.Id.ToString()));
    }

    [Fact]
    public async Task RunAsync_ConfigGone_SkipsWithoutCrawlingAsync()
    {
        var result = await RunAsync(Guid.NewGuid());

        result.Should().BeOfType<CrawlSkipped>();
        await pipeline.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task RunAsync_RootMissing_FailsOnlyThisItemByThrowingAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct)
            .Returns<CrawlResult>(_ => throw new DirectoryNotFoundException("/data/originals/mira"));

        var act = () => RunAsync(config.Id);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        await reporter.DidNotReceiveWithAnyArgs().ReportAsync(default!, default!, Ct);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
    }

    [Fact]
    public async Task RunAsync_ShutdownMidCrawl_PropagatesTheCancellationAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct).Returns<CrawlResult>(_ => throw new OperationCanceledException());

        var act = () => RunAsync(config.Id);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<BurstDetectionPayload>(default!, default, Ct);
    }
}
