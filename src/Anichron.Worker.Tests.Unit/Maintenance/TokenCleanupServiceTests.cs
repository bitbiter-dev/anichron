using Anichron.Core.Data.Repository;
using Anichron.Testing.DependencyInjection;
using Anichron.Testing.Time;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Maintenance;

public sealed class TokenCleanupServiceTests
{
    private sealed class TestFixture
    {
        public IRefreshTokenRepository Repository { get; } = Substitute.For<IRefreshTokenRepository>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public Instant Now { get; }

        public IServiceScopeFactory ScopeFactory { get; }

        public TestFixture()
        {
            Now = Instant.FromUtc(2026, 5, 12, 10, 0, 0);
            Clock.GetCurrentInstant().Returns(Now);

            ScopeFactory = ScopeFactories.Providing(services => services.AddSingleton(Repository));
        }

        public TokenCleanupService Build(
            TimeProvider? timeProvider = null, double intervalHours = 24, ILogger<TokenCleanupService>? logger = null)
        {
            var settings = Options.Create(new WorkerSettings { TokenCleanupIntervalHours = intervalHours });
            return new TokenCleanupService(
                ScopeFactory, Clock, timeProvider ?? new FakeTimeProvider(), settings,
                logger ?? Substitute.For<ILogger<TokenCleanupService>>());
        }
    }

    // ==========================================================================
    // RunCleanupAsync
    // ==========================================================================

    [Fact]
    public async Task RunCleanupAsync_CallsDeleteExpiredWithCurrentInstantAsync()
    {
        var fixture = new TestFixture();
        var service = fixture.Build();

        await service.RunCleanupAsync(CancellationToken.None);

        await fixture.Repository.Received(1).DeleteExpiredAsync(fixture.Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task RunCleanupAsync_LogsDeletedCountAtInformationAsync(int deletedCount)
    {
        var fixture = new TestFixture();
        fixture.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(deletedCount);
        var logger = new FakeLogger<TokenCleanupService>();
        var service = fixture.Build(logger: logger);

        await service.RunCleanupAsync(CancellationToken.None);

        logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(
                entry => entry.Level == LogLevel.Information && entry.Message.Contains(deletedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task RunCleanupAsync_PropagatesRepositoryExceptionAsync()
    {
        var fixture = new TestFixture();
        fixture.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));
        var service = fixture.Build();

        var act = async () => await service.RunCleanupAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    // ==========================================================================
    // ExecuteAsync — schedule
    // ==========================================================================

    [Fact]
    public async Task ExecuteAsync_Started_CleansUpAtOnceThenWaitsTheConfiguredIntervalAsync()
    {
        await using var timeline = new Timeline();
        var fixture = new TestFixture();
        RecordCleanups(fixture, timeline);

        await StartAsync(timeline, fixture.Build(timeline.Clock, intervalHours: 2));

        await timeline.ExpectAsync(new Called("cleanup"), new TimerArmed(TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task ExecuteAsync_CleanupFails_LogsItAndCleansUpAgainAfterTheIntervalAsync()
    {
        await using var timeline = new Timeline();
        var fixture = new TestFixture();
        var calls = 0;
        RecordCleanups(fixture, timeline, () => ++calls == 1 ? throw new InvalidOperationException("DB error") : 0);
        var logger = new FakeLogger<TokenCleanupService>();
        await StartAsync(timeline, fixture.Build(timeline.Clock, intervalHours: 1, logger));
        await timeline.ExpectAsync(new Called("cleanup"), new TimerArmed(TimeSpan.FromHours(1)));

        timeline.Clock.Advance(TimeSpan.FromHours(1));

        await timeline.ExpectAsync(
            new TimerFired(TimeSpan.FromHours(1)), new Called("cleanup"), new TimerArmed(TimeSpan.FromHours(1)));
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CleanupCancelled_StopsInsteadOfLoggingAFailureAsync()
    {
        await using var timeline = new Timeline();
        var fixture = new TestFixture();
        RecordCleanups(fixture, timeline, () => throw new OperationCanceledException());
        var logger = new FakeLogger<TokenCleanupService>();

        await StartAsync(timeline, fixture.Build(timeline.Clock, intervalHours: 1, logger));

        await timeline.ExpectAsync(new Called("cleanup"), new Finished(TaskStatus.Canceled));
        logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Stopped_EndsWithoutFaultingAsync()
    {
        await using var timeline = new Timeline();
        var fixture = new TestFixture();
        RecordCleanups(fixture, timeline);
        var service = fixture.Build(timeline.Clock);
        await StartAsync(timeline, service);
        await timeline.ExpectAsync(new Called("cleanup"), new TimerArmed(TimeSpan.FromHours(24)));

        await service.StopAsync(Ct);

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void RecordCleanups(TestFixture fixture, Timeline timeline, Func<int>? outcome = null)
        => fixture.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                timeline.Record("cleanup");
                return outcome?.Invoke() ?? 0;
            });

    private static async Task StartAsync(Timeline timeline, TokenCleanupService service)
    {
        await service.StartAsync(Ct);
        timeline.Watch(service.ExecuteTask!, () => service.StopAsync(CancellationToken.None));
    }
}
