using Anichron.Infrastructure.Data;
using Microsoft.Extensions.Time.Testing;

namespace Anichron.Infrastructure.Tests.Unit.Data;

public sealed class AdvisoryLockMigratorTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider time = new();
    private readonly IAdvisoryLock advisoryLock = Substitute.For<IAdvisoryLock>();
    private int migrations;

    private Task RunAsync(Func<CancellationToken, Task>? migrate = null, int maxAttempts = 3)
        => new AdvisoryLockMigrator(advisoryLock, time, maxAttempts)
            .RunAsync(migrate ?? (_ => { migrations++; return Task.CompletedTask; }), TestContext.Current.CancellationToken);

    [Fact]
    public async Task RunAsync_LockFree_MigratesOnceAndReleasesAsync()
    {
        advisoryLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);

        await RunAsync();

        migrations.Should().Be(1);
        await advisoryLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_LockHeldOnce_PollsAgainAfterOneSecondAsync()
    {
        advisoryLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(false, true);

        var run = RunAsync();
        time.Advance(PollInterval - TimeSpan.FromTicks(1));
        await advisoryLock.Received(1).TryAcquireAsync(Arg.Any<CancellationToken>());
        migrations.Should().Be(0);

        time.Advance(TimeSpan.FromTicks(1));
        await run.WaitAsync(Settle, TestContext.Current.CancellationToken);

        await advisoryLock.Received(2).TryAcquireAsync(Arg.Any<CancellationToken>());
        migrations.Should().Be(1);
        await advisoryLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_LockNeverFree_TimesOutAfterMaxAttemptsWithoutAFinalWaitAsync()
    {
        advisoryLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(false);

        var run = RunAsync(maxAttempts: 3);
        time.Advance(PollInterval);
        time.Advance(PollInterval);
        var act = () => run.WaitAsync(Settle, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("Could not acquire the migration advisory lock after 3 attempts.");
        await advisoryLock.Received(3).TryAcquireAsync(Arg.Any<CancellationToken>());
        migrations.Should().Be(0);
        await advisoryLock.DidNotReceive().ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MigrationFails_StillReleasesTheLockAndDoesNotRetryAsync()
    {
        advisoryLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        var failure = new InvalidOperationException("migration failed");

        var act = () => RunAsync(_ => Task.FromException(failure));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        await advisoryLock.Received(1).TryAcquireAsync(Arg.Any<CancellationToken>());
        await advisoryLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }
}
