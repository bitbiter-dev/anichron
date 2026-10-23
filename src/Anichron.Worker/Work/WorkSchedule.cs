using Anichron.Core.Work;
using NodaTime;

namespace Anichron.Worker.Work;

public interface IWorkSchedule
{
    string DedupKey { get; }

    Duration Interval { get; }

    Task EnqueueAsync(IWorkQueue queue, CancellationToken ct);
}

/// <summary>The periodic schedules a Worker serves, asked again at every scheduler tick.</summary>
public interface IWorkScheduleSource
{
    Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(IServiceProvider services, CancellationToken ct);
}

public class WorkSchedule<TPayload>(TPayload payload, Duration interval) : IWorkSchedule, IWorkScheduleSource
    where TPayload : IWorkPayload
{
    public string DedupKey { get; } = payload.DedupKey
        ?? throw new InvalidOperationException($"A periodic {TPayload.Kind} item needs a dedup key.");

    public Duration Interval => interval;

    public Task EnqueueAsync(IWorkQueue queue, CancellationToken ct) => queue.EnqueueAsync(payload, null, ct);

    public Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(IServiceProvider services, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<IWorkSchedule>>([this]);
}
