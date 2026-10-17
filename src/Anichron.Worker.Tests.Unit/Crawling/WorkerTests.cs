using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Time;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using CrawlingWorker = Anichron.Worker.Crawling.Worker;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class WorkerTests
{
    private const int IntervalHours = 3;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TestFixture
    {
        public IUserStorageConfigRepository ConfigRepository { get; } = Substitute.For<IUserStorageConfigRepository>();
        public IFileIngestionPipeline Pipeline { get; } = Substitute.For<IFileIngestionPipeline>();
        public WorkerState WorkerState { get; } = new();

        private readonly IServiceScopeFactory scopeFactory;

        public TestFixture()
        {
            var serviceProvider = Substitute.For<IServiceProvider>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);

            serviceProvider.GetService(typeof(IUserStorageConfigRepository)).Returns(ConfigRepository);

            ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            ConfigRepository.GetActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public CrawlingWorker Build(TimeProvider? timeProvider = null)
            => new(
                scopeFactory,
                WorkerState,
                Pipeline,
                timeProvider ?? new FakeTimeProvider(),
                Options.Create(new WorkerSettings { CrawlIntervalHours = IntervalHours }),
                Substitute.For<ILogger<CrawlingWorker>>());
    }

    // ==========================================================================
    // ExecuteAsync — crawl schedule
    // ==========================================================================

    [Fact]
    public async Task ExecuteAsync_Started_CrawlsAtOnceThenWaitsTheConfiguredIntervalAsync()
    {
        await using var timeline = new Timeline();
        var worker = await StartAsync(timeline);

        await timeline.ExpectAsync(new Called("crawl"), new TimerArmed(TimeSpan.FromHours(3)));
        worker.ExecuteTask!.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Stopped_EndsWithoutCrawlingAgainAsync()
    {
        await using var timeline = new Timeline();
        var worker = await StartAsync(timeline);
        await timeline.ExpectAsync(new Called("crawl"), new TimerArmed(TimeSpan.FromHours(3)));

        await worker.StopAsync(Ct);
        timeline.Clock.Advance(TimeSpan.FromHours(3));

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
        timeline.Unread.Should().BeEmpty();
    }

    private static async Task<CrawlingWorker> StartAsync(Timeline timeline)
    {
        var fixture = new TestFixture();
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                timeline.Record("crawl");
                return [];
            });
        var worker = fixture.Build(timeline.Clock);
        await worker.StartAsync(Ct);
        timeline.Watch(worker.ExecuteTask!, () => worker.StopAsync(CancellationToken.None));
        return worker;
    }

    // ==========================================================================
    // All-user mode
    // ==========================================================================

    [Fact]
    public async Task CrawlAllAsync_NoResolvedUserId_CallsGetAllActiveAsync()
    {
        var fixture = new TestFixture();
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(CancellationToken.None);

        await fixture.ConfigRepository.Received(1).GetAllActiveAsync(Arg.Any<CancellationToken>());
        await fixture.ConfigRepository.DidNotReceive()
            .GetActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Dedicated-user mode
    // ==========================================================================

    [Fact]
    public async Task CrawlAllAsync_WithResolvedUserId_CallsGetActiveByUserIdAsync()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        fixture.WorkerState.ResolvedUserId = userId;
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(CancellationToken.None);

        await fixture.ConfigRepository.Received(1)
            .GetActiveByUserIdAsync(userId, Arg.Any<CancellationToken>());
        await fixture.ConfigRepository.DidNotReceive().GetAllActiveAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Pipeline invocation
    // ==========================================================================

    [Fact]
    public async Task CrawlAllAsync_MultipleActiveConfigs_RunsPipelineForEachAsync()
    {
        var fixture = new TestFixture();
        var configs = new List<UserStorageConfig>
        {
            new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas/a" },
            new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas/b" },
        };
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(configs);
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(CancellationToken.None);

        await fixture.Pipeline.Received(2)
            .RunAsync(Arg.Any<UserStorageConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CrawlAllAsync_NoActiveConfigs_PipelineIsNeverCalledAsync()
    {
        var fixture = new TestFixture();
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(CancellationToken.None);

        await fixture.Pipeline.DidNotReceive()
            .RunAsync(Arg.Any<UserStorageConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CrawlAllAsync_ActiveConfig_RunsPipelineWithCorrectConfigAsync()
    {
        var fixture = new TestFixture();
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas/photos" };
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns([config]);
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(CancellationToken.None);

        await fixture.Pipeline.Received(1).RunAsync(config, Arg.Any<CancellationToken>());
    }
}
