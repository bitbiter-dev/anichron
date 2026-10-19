using Anichron.Core.Work;
using NodaTime;

namespace Anichron.Worker.Work;

public interface IWorkSchedule
{
    string DedupKey { get; }

    Duration Interval { get; }

    Task EnqueueAsync(IWorkQueue queue, CancellationToken ct);
}

public abstract class WorkSchedule<TPayload>(TPayload payload, Duration interval) : IWorkSchedule
    where TPayload : IWorkPayload
{
    public string DedupKey { get; } = payload.DedupKey
        ?? throw new InvalidOperationException($"A periodic {TPayload.Kind} item needs a dedup key.");

    public Duration Interval => interval;

    public Task EnqueueAsync(IWorkQueue queue, CancellationToken ct) => queue.EnqueueAsync(payload, null, ct);
}
