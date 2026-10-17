using Anichron.Testing.Time;
using Anichron.Worker.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anichron.Worker.Tests.Unit.Startup;

public sealed class DatabaseMigratorServiceTests : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 10;

    private readonly Timeline timeline = new();
    private readonly IDatabaseMigrator migrator = Substitute.For<IDatabaseMigrator>();
    private readonly DatabaseMigratorService service;
    private Task start = Task.CompletedTask;

    public DatabaseMigratorServiceTests()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IDatabaseMigrator)).Returns(migrator);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        service = new DatabaseMigratorService(scopeFactory, timeline.Clock, Substitute.For<ILogger<DatabaseMigratorService>>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private void Migrations(Func<int, Task> outcome)
    {
        var calls = 0;
        migrator.MigrateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            timeline.Record("migrate");
            return outcome(++calls);
        });
    }

    private void Start()
    {
        start = service.StartAsync(timeline.Lifetime);
        timeline.Watch(start);
    }

    [Fact]
    public Task StartAsync_MigrationSucceeds_MigratesOnceThroughAScopeAsync()
    {
        Migrations(_ => Task.CompletedTask);

        Start();

        return timeline.ExpectAsync(new Called("migrate"), new Finished(TaskStatus.RanToCompletion));
    }

    [Fact]
    public async Task StartAsync_DatabaseNotReady_RetriesAfterFiveSecondsAsync()
    {
        Migrations(call => call == 1 ? Task.FromException(new InvalidOperationException("not ready")) : Task.CompletedTask);
        Start();
        await timeline.ExpectAsync(new Called("migrate"), new TimerArmed(RetryDelay));

        timeline.Clock.Advance(RetryDelay);

        await timeline.ExpectAsync(
            new TimerFired(RetryDelay), new Called("migrate"), new Finished(TaskStatus.RanToCompletion));
    }

    [Fact]
    public async Task StartAsync_DatabaseNeverReady_GivesUpAfterTenAttemptsWrappingTheLastErrorAsync()
    {
        var last = new InvalidOperationException("postgres unavailable");
        Migrations(call => Task.FromException(call == MaxAttempts ? last : new InvalidOperationException("not ready")));
        Start();

        for (var attempt = 1; attempt < MaxAttempts; attempt++)
        {
            await timeline.ExpectAsync(new Called("migrate"), new TimerArmed(RetryDelay));
            timeline.Clock.Advance(RetryDelay);
            await timeline.ExpectAsync(new TimerFired(RetryDelay));
        }

        await timeline.ExpectAsync(new Called("migrate"), new Finished(TaskStatus.Faulted));
        start.Exception!.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Should().Match<InvalidOperationException>(ex =>
                ex.Message == "Database unavailable after 10 attempts. Aborting." && ex.InnerException == last);
    }

    [Fact]
    public void StopAsync_CompletesAtOnce()
        => service.StopAsync(Ct).IsCompletedSuccessfully.Should().BeTrue();
}
