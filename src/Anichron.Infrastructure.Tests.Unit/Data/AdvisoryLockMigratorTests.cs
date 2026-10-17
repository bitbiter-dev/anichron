using Anichron.Infrastructure.Data;
using Anichron.Testing.Time;

namespace Anichron.Infrastructure.Tests.Unit.Data;

public sealed class AdvisoryLockMigratorTests : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Timeline timeline = new();
    private readonly IAdvisoryLock advisoryLock = Substitute.For<IAdvisoryLock>();
    private Task run = Task.CompletedTask;

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private void LockIsFree(params bool[] answers)
        => advisoryLock.TryAcquireAsync(Arg.Any<CancellationToken>())
            .Returns(answers[0], answers[1..])
            .AndDoes(_ => timeline.Record("acquire"));

    private void Start(Func<CancellationToken, Task>? migrate = null, int maxAttempts = 3)
    {
        run = new AdvisoryLockMigrator(advisoryLock, timeline.Clock, maxAttempts).RunAsync(
            migrate ?? (_ => { timeline.Record("migrate"); return Task.CompletedTask; }),
            timeline.Lifetime);
        timeline.Watch(run);
    }

    [Fact]
    public async Task RunAsync_LockFree_MigratesOnceAndReleasesAsync()
    {
        LockIsFree(true);

        Start();

        await timeline.ExpectAsync(
            new Called("acquire"), new Called("migrate"), new Finished(TaskStatus.RanToCompletion));
        await advisoryLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_LockHeldOnce_PollsAgainAfterOneSecondAsync()
    {
        LockIsFree(false, true);
        Start();
        await timeline.ExpectAsync(new Called("acquire"), new TimerArmed(PollInterval));

        timeline.Clock.Advance(PollInterval - TimeSpan.FromTicks(1));
        timeline.Unread.Should().BeEmpty();
        timeline.Clock.Advance(TimeSpan.FromTicks(1));

        await timeline.ExpectAsync(
            new TimerFired(PollInterval), new Called("acquire"), new Called("migrate"), new Finished(TaskStatus.RanToCompletion));
        await advisoryLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_LockNeverFree_TimesOutAfterMaxAttemptsWithoutAFinalWaitAsync()
    {
        LockIsFree(false);
        Start(maxAttempts: 3);

        await timeline.ExpectAsync(new Called("acquire"), new TimerArmed(PollInterval));
        timeline.Clock.Advance(PollInterval);
        await timeline.ExpectAsync(new TimerFired(PollInterval), new Called("acquire"), new TimerArmed(PollInterval));
        timeline.Clock.Advance(PollInterval);

        await timeline.ExpectAsync(
            new TimerFired(PollInterval), new Called("acquire"), new Finished(TaskStatus.Faulted));
        run.Exception!.InnerException.Should().BeOfType<TimeoutException>()
            .Which.Message.Should().Be("Could not acquire the migration advisory lock after 3 attempts.");
        await advisoryLock.DidNotReceive().ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MigrationFails_StillReleasesTheLockAndDoesNotRetryAsync()
    {
        LockIsFree(true);
        var failure = new InvalidOperationException("migration failed");

        Start(_ => Task.FromException(failure));

        await timeline.ExpectAsync(new Called("acquire"), new Finished(TaskStatus.Faulted));
        run.Exception!.InnerException.Should().BeSameAs(failure);
        await advisoryLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }
}
