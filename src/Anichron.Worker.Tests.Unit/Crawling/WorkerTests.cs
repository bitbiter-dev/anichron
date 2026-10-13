using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Anichron.Worker.Tests.Unit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrawlingWorker = Anichron.Worker.Crawling.Worker;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class WorkerTests
{
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

        public SignallingTimeProvider Time { get; } = new();

        public CrawlingWorker Build()
            => new(
                scopeFactory,
                WorkerState,
                Pipeline,
                Time,
                Options.Create(new WorkerSettings()),
                Substitute.For<ILogger<CrawlingWorker>>());
    }

    // ==========================================================================
    // ExecuteAsync — crawl schedule
    // ==========================================================================

    [Fact]
    public async Task ExecuteAsync_Started_CrawlsImmediatelyThenAgainAfterTheIntervalAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        using var crawls = new SemaphoreSlim(0);
        fixture.ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { crawls.Release(); return []; });
        var interval = TimeSpan.FromHours(new WorkerSettings().CrawlIntervalHours);
        var worker = fixture.Build();

        await worker.StartAsync(ct);
        await fixture.Time.WaitForTimerAsync(ct);
        (await crawls.WaitAsync(0, ct)).Should().BeTrue();

        fixture.Time.Advance(interval - TimeSpan.FromTicks(1));
        (await crawls.WaitAsync(0, ct)).Should().BeFalse();
        fixture.Time.Advance(TimeSpan.FromTicks(1));
        (await crawls.WaitAsync(TimeSpan.FromSeconds(5), ct)).Should().BeTrue();

        await worker.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_Stopped_DoesNotCrawlAgainAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var worker = fixture.Build();

        await worker.StartAsync(ct);
        await fixture.Time.WaitForTimerAsync(ct);
        await worker.StopAsync(ct);
        fixture.Time.Advance(TimeSpan.FromHours(new WorkerSettings().CrawlIntervalHours));

        await fixture.ConfigRepository.Received(1).GetAllActiveAsync(Arg.Any<CancellationToken>());
        worker.ExecuteTask!.IsCompleted.Should().BeTrue();
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
