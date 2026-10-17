using Anichron.Testing.Time;
using Anichron.Worker.Scheduling;

namespace Anichron.Worker.Tests.Unit.Scheduling;

public sealed class RecurringWorkTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    private static Func<CancellationToken, Task> Work(Timeline timeline)
        => async token =>
        {
            timeline.Record("work");
            await Task.Yield();
            token.ThrowIfCancellationRequested();
        };

    private static Task StartAsync(Timeline timeline, Func<CancellationToken, Task> work, CancellationToken ct)
        => RecurringWork.RunAsync(work, Interval, timeline.Clock, ct);

    [Fact]
    public async Task RunAsync_Started_RunsTheWorkAtOnceThenWaitsOneIntervalAsync()
    {
        await using var timeline = new Timeline();

        timeline.Watch(StartAsync(timeline, Work(timeline), timeline.Lifetime));

        await timeline.ExpectAsync(new Called("work"), new TimerArmed(Interval));
    }

    [Fact]
    public async Task RunAsync_IntervalElapses_RunsTheWorkAgainAndWaitsAnotherIntervalAsync()
    {
        await using var timeline = new Timeline();
        timeline.Watch(StartAsync(timeline, Work(timeline), timeline.Lifetime));
        await timeline.ExpectAsync(new Called("work"), new TimerArmed(Interval));

        timeline.Clock.Advance(Interval - TimeSpan.FromTicks(1));
        timeline.Unread.Should().BeEmpty();
        timeline.Clock.Advance(TimeSpan.FromTicks(1));

        await timeline.ExpectAsync(
            new TimerFired(Interval), new Called("work"), new TimerArmed(Interval));
    }

    [Fact]
    public async Task RunAsync_WorkFails_EndsWithThatFailureAndRunsNoMoreAsync()
    {
        await using var timeline = new Timeline();
        var failure = new InvalidOperationException("work failed");

        var run = StartAsync(timeline, _ => Task.FromException(failure), timeline.Lifetime);
        timeline.Watch(run);

        await timeline.ExpectAsync(new Finished(TaskStatus.Faulted));
        run.Exception!.InnerExceptions.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileWaiting_EndsCancelledWithoutRunningTheWorkAgainAsync()
    {
        using var stop = new CancellationTokenSource();
        await using var timeline = new Timeline();
        timeline.Watch(StartAsync(timeline, Work(timeline), stop.Token), stop.CancelAsync);
        await timeline.ExpectAsync(new Called("work"), new TimerArmed(Interval));

        await stop.CancelAsync();

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelled_EndsWithoutRunningTheWorkAsync()
    {
        await using var timeline = new Timeline();

        timeline.Watch(StartAsync(timeline, Work(timeline), new CancellationToken(canceled: true)));

        await timeline.ExpectAsync(new Finished(TaskStatus.RanToCompletion));
    }

    [Fact]
    public async Task RunAsync_PassesItsTokenToTheWorkAsync()
    {
        await using var timeline = new Timeline();
        CancellationToken seen = default;

        timeline.Watch(StartAsync(timeline, async token => { seen = token; await Work(timeline)(token); }, timeline.Lifetime));
        await timeline.ExpectAsync(new Called("work"), new TimerArmed(Interval));

        seen.Should().Be(timeline.Lifetime);
    }
}
