using Anichron.Core.Work;
using Anichron.Worker.Scheduling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Work;

public sealed class WorkScheduler(
    IWorkItemStore store,
    IEnumerable<IWorkScheduleSource> sources,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<WorkerSettings> options,
    ILogger<WorkScheduler> logger) : BackgroundService
{
    private readonly WorkerSettings settings = options.Value;
    private readonly OutageLog outage = new(logger, "Scheduling work or recovering leases");

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => RecurringWork.RunAsync(
            TickOrLogAsync, TimeSpan.FromSeconds(settings.WorkPollIntervalSeconds), timeProvider, stoppingToken);

    private async Task TickOrLogAsync(CancellationToken ct)
    {
        try
        {
            await TickAsync(ct);
            outage.Succeeded();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            outage.Failed(ex);
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
}
