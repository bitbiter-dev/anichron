using Anichron.Core.Data.Repository;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Tests.Unit;

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

            var serviceProvider = Substitute.For<IServiceProvider>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            ScopeFactory = Substitute.For<IServiceScopeFactory>();
            ScopeFactory.CreateScope().Returns(scope);

            serviceProvider.GetService(typeof(IRefreshTokenRepository)).Returns(Repository);
        }

        public TokenCleanupService Build(double intervalHours = 24)
        {
            var settings = Options.Create(new WorkerSettings { TokenCleanupIntervalHours = intervalHours });
            return new TokenCleanupService(ScopeFactory, Clock, settings, Substitute.For<ILogger<TokenCleanupService>>());
        }
    }

    // ==========================================================================
    // RunCleanupAsync
    // ==========================================================================

    [Fact]
    public async Task RunCleanupAsync_CallsDeleteExpiredWithCurrentInstant()
    {
        var fx = new TestFixture();
        var sut = fx.Build();

        await sut.RunCleanupAsync(CancellationToken.None);

        await fx.Repository.Received(1).DeleteExpiredAsync(fx.Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task RunCleanupAsync_LogsDeletedCountAtInformation(int deletedCount)
    {
        var fx = new TestFixture();
        fx.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(deletedCount);
        var logger = new CapturingLogger();
        var sut = new TokenCleanupService(fx.ScopeFactory, fx.Clock, Options.Create(new WorkerSettings()), logger);

        await sut.RunCleanupAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle()
            .Which.Should().Match<(LogLevel Level, string Message)>(
                e => e.Level == LogLevel.Information && e.Message.Contains(deletedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task RunCleanupAsync_PropagatesRepositoryException()
    {
        var fx = new TestFixture();
        fx.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));
        var sut = fx.Build();

        var act = async () => await sut.RunCleanupAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    private sealed class CapturingLogger : ILogger<TokenCleanupService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
