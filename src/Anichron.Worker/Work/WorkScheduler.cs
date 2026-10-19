using Anichron.Core.Work;
using Anichron.Worker.Scheduling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Work;

public sealed partial class WorkScheduler(
    IWorkItemStore store,
    IEnumerable<IWorkScheduleSource> sources,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<WorkerSettings> options,
    ILogger<WorkScheduler> logger) : BackgroundService
{
    private readonly WorkerSettings settings = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => RecurringWork.RunAsync(
            TickOrLogAsync, TimeSpan.FromSeconds(settings.WorkPollIntervalSeconds), timeProvider, stoppingToken);

    private async Task TickOrLogAsync(CancellationToken ct)
    {
        try
        {
            await TickAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.TickFailed(logger, ex);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
        await store.RecoverExpiredLeasesAsync(now, settings.WorkMaxStalls, ct);
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkQueue>();
        foreach (var source in sources)
        {
            foreach (var schedule in await source.SchedulesAsync(scope.ServiceProvider, ct))
            {
                if (await store.IsDueAsync(schedule.DedupKey, now - schedule.Interval, ct))
                    await schedule.EnqueueAsync(queue, ct);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Scheduling work or recovering leases failed; will retry at the next poll.")]
        public static partial void TickFailed(ILogger logger, Exception ex);
    }
}
