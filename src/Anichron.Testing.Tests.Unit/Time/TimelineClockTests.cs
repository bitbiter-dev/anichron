using Anichron.Testing.Time;

namespace Anichron.Testing.Tests.Unit.Time;

public sealed class TimelineClockTests
{
    private static readonly TimeSpan Due = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortSilence = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delay_RecordsTheTimerItArmsAsync()
    {
        await using var timeline = new Timeline(ShortSilence);

        _ = Task.Delay(Due, timeline.Clock, Ct);

        await timeline.ExpectAsync(new TimerArmed(Due));
    }

    [Fact]
    public async Task Advance_ShortOfTheDueTime_FiresNothingAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        var delay = Task.Delay(Due, timeline.Clock, Ct);
        await timeline.ExpectAsync(new TimerArmed(Due));

        timeline.Clock.Advance(Due - TimeSpan.FromTicks(1));

        timeline.Unread.Should().BeEmpty();
        delay.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Advance_ToTheDueTime_RecordsTheFiringBeforeTheDelayEndsAsync()
    {
        await using var timeline = new Timeline(ShortSilence);
        var delay = Task.Delay(Due, timeline.Clock, Ct);
        timeline.Watch(delay);
        await timeline.ExpectAsync(new TimerArmed(Due));

        timeline.Clock.Advance(Due);

        await timeline.ExpectAsync(new TimerFired(Due), new Finished(TaskStatus.RanToCompletion));
        delay.IsCompletedSuccessfully.Should().BeTrue();
    }
}
