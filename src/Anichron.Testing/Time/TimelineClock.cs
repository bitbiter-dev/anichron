using Microsoft.Extensions.Time.Testing;

namespace Anichron.Testing.Time;

internal sealed class TimelineClock(Timeline timeline) : FakeTimeProvider
{
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        timeline.Append(new TimerArmed(dueTime));
        return base.CreateTimer(
            s =>
            {
                timeline.Append(new TimerFired(dueTime));
                callback(s);
            },
            state,
            dueTime,
            period);
    }
}
