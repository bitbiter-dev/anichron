using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class CrawlTests
{
    private static readonly Guid MiraId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly IUserStorageConfigRepository configs = Substitute.For<IUserStorageConfigRepository>();
    private readonly IFileIngestionPipeline pipeline = Substitute.For<IFileIngestionPipeline>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
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
        => new CrawlHandler(configs, pipeline, logger).RunAsync(new CrawlPayload(configId), progress, Ct);

    // ==========================================================================
    // CrawlHandler
    // ==========================================================================

    [Fact]
    public async Task RunAsync_ActiveConfig_CrawlsItAndReturnsTheCountsAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct).Returns(new CrawlResult(5, 4, 1));

        var result = await RunAsync(config.Id);

        result.Should().Be(new CrawlResult(5, 4, 1));
        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Information && e.Message.Contains("/data/originals/mira"));
    }

    [Fact]
    public async Task RunAsync_InactiveConfig_SkipsWithoutCrawlingAsync()
    {
        var config = Config(active: false);
        configs.FindByIdAsync(config.Id, Ct).Returns(config);

        var result = await RunAsync(config.Id);

        result.Should().Be(new CrawlSkipped("The storage config is missing or inactive."));
        await pipeline.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, Ct);
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
    }

    [Fact]
    public async Task RunAsync_ShutdownMidCrawl_PropagatesTheCancellationAsync()
    {
        var config = Config();
        configs.FindByIdAsync(config.Id, Ct).Returns(config);
        pipeline.RunAsync(config, progress, Ct).Returns<CrawlResult>(_ => throw new OperationCanceledException());

        var act = () => RunAsync(config.Id);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ==========================================================================
    // CrawlSchedules
    // ==========================================================================

    private static async Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(
        CrawlSchedules source, IUserStorageConfigRepository repository)
    {
        await using var services = new ServiceCollection().AddSingleton(repository).BuildServiceProvider();
        return await source.SchedulesAsync(services, Ct);
    }

    [Fact]
    public async Task SchedulesAsync_AllUserWorker_SchedulesEveryActiveConfigAtTheCrawlIntervalAsync()
    {
        var (a, b) = (Config(), Config());
        configs.GetAllActiveAsync(Ct).Returns([a, b]);
        var source = new CrawlSchedules(new WorkerState(), Options.Create(new WorkerSettings { CrawlIntervalHours = 6 }));

        var schedules = await SchedulesAsync(source, configs);

        schedules.Select(s => (s.DedupKey, s.Interval)).Should().Equal(
            ($"crawl:{a.Id}", Duration.FromHours(6)), ($"crawl:{b.Id}", Duration.FromHours(6)));
    }

    [Fact]
    public async Task SchedulesAsync_PerUserWorker_SchedulesOnlyItsUsersActiveConfigsAsync()
    {
        var mine = Config();
        configs.GetActiveByUserIdAsync(MiraId, Ct).Returns([mine]);
        configs.GetAllActiveAsync(Ct).Returns([mine, Config()]);
        var state = new WorkerState();
        var source = new CrawlSchedules(state, Options.Create(new WorkerSettings()));
        state.ResolvedUserId = MiraId;

        var schedules = await SchedulesAsync(source, configs);

        schedules.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { DedupKey = $"crawl:{mine.Id}", Interval = Duration.FromHours(4) });
    }

    [Fact]
    public async Task CrawlSchedule_EnqueuesACrawlOfItsConfigAsync()
    {
        var configId = Guid.NewGuid();
        var queue = Substitute.For<IWorkQueue>();

        await new CrawlSchedule(configId, Duration.FromHours(4)).EnqueueAsync(queue, Ct);

        await queue.Received(1).EnqueueAsync(new CrawlPayload(configId), null, Ct);
    }
}
