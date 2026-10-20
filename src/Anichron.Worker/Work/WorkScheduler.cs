using Anichron.Core.Work;
using Anichron.Worker.Scheduling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Work;

public sealed partial class WorkScheduler(
    IWorkItemStore store,
    IEnumerable<IWorkSchedule> schedules,
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
        foreach (var schedule in schedules)
        {
            if (!await store.IsDueAsync(schedule.DedupKey, now - schedule.Interval, ct))
                continue;

            await using var scope = scopeFactory.CreateAsyncScope();
            await schedule.EnqueueAsync(scope.ServiceProvider.GetRequiredService<IWorkQueue>(), ct);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Scheduling work or recovering leases failed; will retry at the next poll.")]
        public static partial void TickFailed(ILogger logger, Exception ex);
    }
}
