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
    public async Task StartAsync_MigrationSucceedsFirstAttempt_ReturnsWithoutExceptionAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var service = fixture.Build();

        await service.StartAsync(ct);

        await fixture.Migrator.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Retry then success
    // ==========================================================================

    [Fact]
    public async Task StartAsync_DatabaseNotReadyThenReady_RetriesAndSucceedsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var callCount = 0;
        fixture.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            return callCount < 3 ? throw new InvalidOperationException("DB not ready yet") : Task.CompletedTask;
        });
        var service = fixture.Build();

        await service.StartAsync(ct);

        await fixture.Migrator.Received(3).MigrateAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // All attempts exhausted
    // ==========================================================================

    [Fact]
    public async Task StartAsync_AllAttemptsExhausted_ThrowsInvalidOperationExceptionAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException("postgres unavailable"));
        var service = fixture.Build();

        var act = async () => await service.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*10 attempts*");
    }

    [Fact]
    public async Task StartAsync_AllAttemptsExhausted_OriginalExceptionIsInnerExceptionAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var originalException = new InvalidOperationException("postgres unavailable");
        fixture.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ => throw originalException);
        var service = fixture.Build();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(ct));

        thrown.InnerException.Should().BeSameAs(originalException);
    }

    [Fact]
    public async Task StartAsync_AllAttemptsExhausted_MigrateCalledMaxAttemptsTimesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.Migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException());
        var service = fixture.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(ct));

        await fixture.Migrator.Received(10).MigrateAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // StopAsync
    // ==========================================================================

    [Fact]
    public async Task StopAsync_AlwaysCompletesSuccessfullyAsync()
    {
        var service = new TestFixture().Build();
        var act = async () => await service.StopAsync(TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }
}
