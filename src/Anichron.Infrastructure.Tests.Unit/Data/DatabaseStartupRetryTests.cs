using Anichron.Infrastructure.Data;
using Anichron.Testing.Time;

namespace Anichron.Infrastructure.Tests.Unit.Data;

public sealed class DatabaseStartupRetryTests : IAsyncDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private readonly Timeline timeline = new();
    private readonly CancellationTokenSource stop = new();
    private readonly List<(Exception Exception, int Attempt)> retries = [];
    private readonly List<Exception> giveUps = [];
    private Task run = Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await timeline.DisposeAsync();
        stop.Dispose();
    }

    private void Start(Func<int, Task> attempt)
    {
        var calls = 0;
        run = new DatabaseStartupRetry(timeline.Clock, 3, Delay).RunAsync(
            _ =>
            {
                timeline.Record("attempt");
                return attempt(++calls);
            },
            (ex, number) => retries.Add((ex, number)),
            giveUps.Add,
            stop.Token);
        timeline.Watch(run, stop.CancelAsync);
    }

    [Fact]
    public async Task RunAsync_FirstAttemptSucceeds_RunsOnceWithoutRetryingAsync()
    {
        Start(_ => Task.CompletedTask);

        await timeline.ExpectAsync(new Called("attempt"), new Finished(TaskStatus.RanToCompletion));
        retries.Should().BeEmpty();
        giveUps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_FirstAttemptFails_WaitsTheFullDelayBeforeRetryingAsync()
    {
        var failure = new InvalidOperationException("not ready");
        Start(call => call == 1 ? Task.FromException(failure) : Task.CompletedTask);

        await timeline.ExpectAsync(new Called("attempt"), new TimerArmed(Delay));
        retries.Should().Equal((failure, 1));
        timeline.Clock.Advance(Delay - TimeSpan.FromTicks(1));
        timeline.Unread.Should().BeEmpty();
        timeline.Clock.Advance(TimeSpan.FromTicks(1));

        await timeline.ExpectAsync(
            new TimerFired(Delay), new Called("attempt"), new Finished(TaskStatus.RanToCompletion));
        giveUps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_EveryAttemptFails_GivesUpAfterMaxAttemptsWrappingTheLastErrorAsync()
    {
        var failures = new[] { new TimeoutException("1"), new TimeoutException("2"), new TimeoutException("3") };
        Start(call => Task.FromException(failures[call - 1]));

        await timeline.ExpectAsync(new Called("attempt"), new TimerArmed(Delay));
        timeline.Clock.Advance(Delay);
        await timeline.ExpectAsync(new TimerFired(Delay), new Called("attempt"), new TimerArmed(Delay));
        timeline.Clock.Advance(Delay);

        await timeline.ExpectAsync(
            new TimerFired(Delay), new Called("attempt"), new Finished(TaskStatus.Faulted));
        run.Exception!.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Should().Match<InvalidOperationException>(ex =>
                ex.Message == "Database unavailable after 3 attempts. Aborting." && ex.InnerException == failures[2]);
        retries.Should().Equal((failures[0], 1), (failures[1], 2));
        giveUps.Should().Equal(failures[2]);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileWaiting_StopsWithoutAnotherAttemptAsync()
    {
        Start(_ => Task.FromException(new InvalidOperationException()));
        await timeline.ExpectAsync(new Called("attempt"), new TimerArmed(Delay));

        await stop.CancelAsync();

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
        giveUps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PassesTheCallersTokenToTheOperationAsync()
    {
        CancellationToken seen = default;

        run = new DatabaseStartupRetry(timeline.Clock, 3, Delay).RunAsync(
            token => { seen = token; return Task.CompletedTask; }, (_, _) => { }, _ => { }, stop.Token);
        timeline.Watch(run);
        await timeline.ExpectAsync(new Finished(TaskStatus.RanToCompletion));

        seen.Should().Be(stop.Token);
    }
}
