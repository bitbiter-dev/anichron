using Anichron.Testing.Time;

namespace Anichron.Testing.Tests.Unit.Time;

public sealed class TimelineTests
{
    private static readonly TimeSpan ShortSilence = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void DefaultSilenceLimit_IsTenSeconds()
        => Timeline.DefaultSilenceLimit.Should().Be(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task ExpectAsync_EventsRecordedAsExpected_PassesAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        timeline.Record("first");
        timeline.Record("second");

        await timeline.ExpectAsync(new Called("first"), new Called("second"));
    }

    [Fact]
    public async Task ExpectAsync_ReadsOnlyAsManyEventsAsItExpectsAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        timeline.Record("first");
        timeline.Record("second");

        await timeline.ExpectAsync(new Called("first"));

        timeline.Unread.Should().Equal(new Called("second"));
    }

    [Fact]
    public async Task ExpectAsync_StartsAfterWhatEarlierExpectationsReadAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        timeline.Record("first");
        await timeline.ExpectAsync(new Called("first"));
        timeline.Record("second");

        await timeline.ExpectAsync(new Called("second"));
    }

    [Fact]
    public async Task ExpectAsync_EventStillToCome_WaitsForItAsync()
    {
        await using var timeline = new Timeline(ShortSilence * 10);
        timeline.Record("first");

        var expectation = timeline.ExpectAsync(new Called("first"), new Called("second"));
        expectation.IsCompleted.Should().BeFalse();
        await Task.Run(() => timeline.Record("second"), Ct);

        await expectation;
    }

    [Fact]
    public async Task ExpectAsync_EventDiffers_FailsAtThatEventWithoutWaitingForTheRestAsync()
    {
        await using var timeline = new Timeline();
        timeline.Record("first");
        timeline.Record("other");

        var act = () => timeline.ExpectAsync(new Called("first"), new Called("second"), new Called("third"));

        await act.Should().ThrowAsync<TimelineMismatchException>()
            .WithMessage("Expected Called { What = second } as event 2 of 3, but the timeline recorded Called { What = other }.");
    }

    [Fact]
    public async Task ExpectAsync_TimelineGoesSilent_ThrowsNamingTheAwaitedEventAsync()
    {
        await using var timeline = new Timeline(ShortSilence);

        var act = () => timeline.ExpectAsync(new Called("never"));

        (await act.Should().ThrowAsync<TimeoutException>()
                .WithMessage("Expected Called { What = never }, but the timeline went silent for 00:00:00.2000000."))
            .WithInnerException<TimeoutException>();
    }

    [Fact]
    public async Task ExpectAsync_NothingExpected_ThrowsAsync()
    {
        await using var timeline = new Timeline(ShortSilence);

        var act = () => timeline.ExpectAsync();

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Unread_ListsOnlyEventsAfterTheLastExpectedOneAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        timeline.Record("read");
        timeline.Record("unread");
        await timeline.ExpectAsync(new Called("read"));

        timeline.Unread.Should().Equal(new Called("unread"));
    }

    [Fact]
    public async Task Watch_TaskEnds_RecordsHowItEndedAsync()
    {
        await using var timeline = new Timeline(ShortSilence);

        timeline.Watch(Task.CompletedTask);
        timeline.Watch(Task.FromException(new InvalidOperationException()));
        timeline.Watch(Task.FromCanceled(new CancellationToken(canceled: true)));

        await timeline.ExpectAsync(
            new Finished(TaskStatus.RanToCompletion),
            new Finished(TaskStatus.Faulted),
            new Finished(TaskStatus.Canceled));
    }

    [Fact]
    public async Task Watch_TaskStillRunning_RecordsItsEndWhenItEndsAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        var running = new TaskCompletionSource();

        timeline.Watch(running.Task);
        timeline.Unread.Should().BeEmpty();
        running.SetResult();

        await timeline.ExpectAsync(new Finished(TaskStatus.RanToCompletion));
    }

    [Fact]
    public async Task DisposeAsync_CancelsTheLifetimeAsync()
    {
        var timeline = new Timeline();
        var lifetime = timeline.Lifetime;

        await timeline.DisposeAsync();

        lifetime.IsCancellationRequested.Should().BeTrue();
        timeline.Invoking(t => t.Lifetime).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_StopsEveryWatchedTaskAndWaitsForItToEndAsync()
    {
        var timeline = new Timeline();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        timeline.Watch(first.Task, () => Task.Run(() => first.SetResult(), Ct));
        timeline.Watch(second.Task, () => Task.Run(async () => { await Task.Yield(); second.SetResult(); }, Ct));

        await timeline.DisposeAsync();

        first.Task.IsCompleted.Should().BeTrue();
        second.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_WatchedTaskEndsOnTheLifetime_WaitsForItAsync()
    {
        var timeline = new Timeline();
        var subject = Task.Delay(Timeout.Infinite, timeline.Lifetime);
        timeline.Watch(subject);

        await timeline.DisposeAsync();

        subject.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_WatchedTaskNeverEnds_ThrowsAfterTheSilenceLimitAsync()
    {
        var timeline = new Timeline(ShortSilence);
        timeline.Watch(new TaskCompletionSource().Task);

        var act = async () => await timeline.DisposeAsync();

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task DisposeAsync_StopFails_ReportsTheFailureAsync()
    {
        var timeline = new Timeline(ShortSilence);
        timeline.Watch(Task.CompletedTask, () => Task.FromException(new InvalidOperationException("stop failed")));

        var act = async () => await timeline.DisposeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("stop failed");
    }
}
