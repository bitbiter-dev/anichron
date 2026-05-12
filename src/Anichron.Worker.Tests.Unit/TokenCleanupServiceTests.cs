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

        private readonly IServiceScopeFactory _scopeFactory;

        public TestFixture()
        {
            Now = Instant.FromUtc(2026, 5, 12, 10, 0, 0);
            Clock.GetCurrentInstant().Returns(Now);

            var serviceProvider = Substitute.For<IServiceProvider>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            _scopeFactory = Substitute.For<IServiceScopeFactory>();
            _scopeFactory.CreateScope().Returns(scope);

            serviceProvider.GetService(typeof(IRefreshTokenRepository)).Returns(Repository);
        }

        public TokenCleanupService Build(int intervalHours = 24)
        {
            var settings = Options.Create(new WorkerSettings { TokenCleanupIntervalHours = intervalHours });
            return new TokenCleanupService(_scopeFactory, Clock, settings, Substitute.For<ILogger<TokenCleanupService>>());
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

    [Fact]
    public async Task RunCleanupAsync_ZeroDeleted_CompletesWithoutException()
    {
        var fx = new TestFixture();
        fx.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(0);
        var sut = fx.Build();

        var act = async () => await sut.RunCleanupAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunCleanupAsync_MultipleExpired_CompletesWithoutException()
    {
        var fx = new TestFixture();
        fx.Repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(42);
        var sut = fx.Build();

        var act = async () => await sut.RunCleanupAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
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
}
