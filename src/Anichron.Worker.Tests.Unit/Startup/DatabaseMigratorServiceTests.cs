using Anichron.Worker.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anichron.Worker.Tests.Unit.Startup;

public sealed class DatabaseMigratorServiceTests
{
    private sealed class TestFixture
    {
        public IDatabaseMigrator Migrator { get; } = Substitute.For<IDatabaseMigrator>();

        private readonly IServiceScopeFactory _scopeFactory;

        public TestFixture()
        {
            var serviceProvider = Substitute.For<IServiceProvider>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            _scopeFactory = Substitute.For<IServiceScopeFactory>();
            _scopeFactory.CreateScope().Returns(scope);

            serviceProvider.GetService(typeof(IDatabaseMigrator)).Returns(Migrator);
        }

        public DatabaseMigratorService Build()
            => new(_scopeFactory, Substitute.For<ILogger<DatabaseMigratorService>>())
            {
                RetryDelay = TimeSpan.Zero,
            };
    }

    // ==========================================================================
    // Happy path
    // ==========================================================================

    [Fact]
    public async Task StartAsync_MigrationSucceedsFirstAttempt_ReturnsWithoutException()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var sut = fx.Build();

        await sut.StartAsync(ct);

        await fx.Migrator.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Retry then success
    // ==========================================================================

    [Fact]
    public async Task StartAsync_DatabaseNotReadyThenReady_RetriesAndSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var callCount = 0;
        fx.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            return callCount < 3 ? throw new InvalidOperationException("DB not ready yet") : Task.CompletedTask;
        });
        var sut = fx.Build();

        await sut.StartAsync(ct);

        await fx.Migrator.Received(3).MigrateAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // All attempts exhausted
    // ==========================================================================

    [Fact]
    public async Task StartAsync_AllAttemptsExhausted_ThrowsInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        fx.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException("postgres unavailable"));
        var sut = fx.Build();

        var act = async () => await sut.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{DatabaseMigratorService.MaxAttempts} attempts*");
    }

    [Fact]
    public async Task StartAsync_AllAttemptsExhausted_OriginalExceptionIsInnerException()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var originalEx = new InvalidOperationException("postgres unavailable");
        fx.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ => throw originalEx);
        var sut = fx.Build();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.StartAsync(ct));

        thrown.InnerException.Should().BeSameAs(originalEx);
    }

    [Fact]
    public async Task StartAsync_AllAttemptsExhausted_MigrateCalledMaxAttemptsTimes()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        fx.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException());
        var sut = fx.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(ct));

        await fx.Migrator.Received(DatabaseMigratorService.MaxAttempts).MigrateAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // StopAsync
    // ==========================================================================

    [Fact]
    public async Task StopAsync_AlwaysCompletesSuccessfully()
    {
        var sut = new TestFixture().Build();
        var act = async () => await sut.StopAsync(TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }
}
