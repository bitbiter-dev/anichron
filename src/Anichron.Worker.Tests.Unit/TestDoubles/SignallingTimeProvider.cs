using Microsoft.Extensions.Time.Testing;
using System.Threading.Channels;

namespace Anichron.Worker.Tests.Unit.TestDoubles;

internal sealed class SignallingTimeProvider : FakeTimeProvider
{
    private readonly Channel<ITimer> timersCreated = Channel.CreateUnbounded<ITimer>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        timersCreated.Writer.TryWrite(timer);
        return timer;
    }

    public Task WaitForTimerAsync(CancellationToken ct)
        => timersCreated.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
}
