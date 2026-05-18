using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
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

        private readonly IServiceScopeFactory _scopeFactory;

        public TestFixture()
        {
            var serviceProvider = Substitute.For<IServiceProvider>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            _scopeFactory = Substitute.For<IServiceScopeFactory>();
            _scopeFactory.CreateScope().Returns(scope);

            serviceProvider.GetService(typeof(IUserStorageConfigRepository)).Returns(ConfigRepository);

            ConfigRepository.GetAllActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            ConfigRepository.GetActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public CrawlingWorker Build()
            => new(
                _scopeFactory,
                WorkerState,
                Pipeline,
                Options.Create(new WorkerSettings()),
                Substitute.For<ILogger<CrawlingWorker>>());
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
