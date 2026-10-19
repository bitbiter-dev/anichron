using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.DependencyInjection;
using Anichron.Testing.Time;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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
        public ISharedContentReporter Reporter { get; } = Substitute.For<ISharedContentReporter>();
        public WorkerState WorkerState { get; } = new();

        private readonly IServiceScopeFactory scopeFactory;

        public TestFixture()
        {
            scopeFactory = ScopeFactories.Providing(services => services
                .AddSingleton(ConfigRepository)
                .AddSingleton(Reporter));

            ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            ConfigRepository.GetActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public FakeLogger<CrawlingWorker> Logger { get; } = new();

        public CrawlingWorker Build(TimeProvider? timeProvider = null)
            => new(
                scopeFactory,
                WorkerState,
                Pipeline,
                timeProvider ?? new FakeTimeProvider(),
                Options.Create(new WorkerSettings { CrawlIntervalHours = IntervalHours }),
                Logger);
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

    // ==========================================================================
    // A failing config
    // ==========================================================================

    [Fact]
    public async Task CrawlAllAsync_ConfigCrawlFails_LogsItsRootAndCrawlsTheNextConfigAsync()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var missing = new UserStorageConfig { Id = Guid.NewGuid(), UserId = userId, RootPath = "/data/originals/mira" };
        var next = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/data/originals/alice" };
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([missing, next]);
        fixture.Pipeline.RunAsync(missing, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DirectoryNotFoundException("gone")));
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(Ct);

        await fixture.Pipeline.Received(1).RunAsync(next, Arg.Any<CancellationToken>());
        await fixture.Reporter.Received(1).ReportAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { missing.Id, next.Id })),
            Arg.Any<CancellationToken>());
        fixture.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error)
            .Which.Should().Match<FakeLogRecord>(r =>
                r.Message == $"Crawl of /data/originals/mira (userId={userId}) failed."
                && r.Exception is DirectoryNotFoundException);
    }

    [Fact]
    public async Task CrawlAllAsync_ShutdownDuringACrawl_StopsWithoutLoggingAFailureAsync()
    {
        var fixture = new TestFixture();
        var first = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/data/originals/mira" };
        var second = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/data/originals/alice" };
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([first, second]);
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        fixture.Pipeline.RunAsync(first, Arg.Any<CancellationToken>()).Returns(Task.FromCanceled(shutdown.Token));
        var crawlingWorker = fixture.Build();

        var act = () => crawlingWorker.CrawlAllAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await fixture.Pipeline.DidNotReceive().RunAsync(second, Arg.Any<CancellationToken>());
        await fixture.Reporter.DidNotReceive().ReportAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        fixture.Logger.Collector.GetSnapshot().Should().NotContain(r => r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task CrawlAllAsync_CancellationWithoutShutdown_IsLoggedAsACrawlFailureAsync()
    {
        var fixture = new TestFixture();
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/data/originals/mira" };
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([config]);
        fixture.Pipeline.RunAsync(config, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException("timed out")));
        var crawlingWorker = fixture.Build();

        await crawlingWorker.CrawlAllAsync(Ct);

        fixture.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error)
            .Which.Exception.Should().BeOfType<OperationCanceledException>();
    }

    // ==========================================================================
    // Duplicate content report
    // ==========================================================================

    [Fact]
    public async Task CrawlAllAsync_AfterEveryConfigIsCrawled_ReportsSharedContentAcrossThemAsync()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var home = new UserStorageConfig { Id = Guid.NewGuid(), UserId = userId, RootPath = "/data/originals/mira" };
        var phone = new UserStorageConfig { Id = Guid.NewGuid(), UserId = userId, RootPath = "/data/originals/mira-phone" };
        fixture.WorkerState.ResolvedUserId = userId;
        fixture.ConfigRepository.GetActiveByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns([home, phone]);

        await fixture.Build().CrawlAllAsync(Ct);

        Received.InOrder(() =>
        {
            _ = fixture.Pipeline.RunAsync(home, Arg.Any<CancellationToken>());
            _ = fixture.Pipeline.RunAsync(phone, Arg.Any<CancellationToken>());
            _ = fixture.Reporter.ReportAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { home.Id, phone.Id })),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task CrawlAllAsync_ReportFails_LogsItWithoutThrowingAsync()
    {
        var fixture = new TestFixture();
        fixture.Reporter.ReportAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        await fixture.Build().CrawlAllAsync(Ct);

        fixture.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error)
            .Which.Should().Match<FakeLogRecord>(r =>
                r.Message == "Reporting duplicate content failed." && r.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task CrawlAllAsync_ShutdownDuringTheReport_StopsWithoutLoggingAFailureAsync()
    {
        var fixture = new TestFixture();
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        fixture.Reporter.ReportAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled(shutdown.Token));

        var act = () => fixture.Build().CrawlAllAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Logger.Collector.GetSnapshot().Should().NotContain(r => r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CrawlFails_KeepsRunningAndCrawlsAgainAfterTheIntervalAsync()
    {
        await using var timeline = new Timeline();
        var fixture = new TestFixture();
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/data/originals/mira" };
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([config]);
        fixture.Pipeline.RunAsync(config, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("crawl");
                return Task.FromException(new DirectoryNotFoundException("gone"));
            });
        var worker = fixture.Build(timeline.Clock);
        await worker.StartAsync(Ct);
        timeline.Watch(worker.ExecuteTask!, () => worker.StopAsync(CancellationToken.None));
        await timeline.ExpectAsync(new Called("crawl"), new TimerArmed(TimeSpan.FromHours(3)));

        timeline.Clock.Advance(TimeSpan.FromHours(3));

        await timeline.ExpectAsync(
            new TimerFired(TimeSpan.FromHours(3)), new Called("crawl"), new TimerArmed(TimeSpan.FromHours(3)));
        worker.ExecuteTask!.IsCompleted.Should().BeFalse();
    }
}
