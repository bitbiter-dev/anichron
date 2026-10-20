using Anichron.Core.Work;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Maintenance;

public sealed record WorkItemRetentionResult(int Deleted, Instant FinishedBefore);

public sealed partial class WorkItemRetentionHandler(
    IWorkItemStore store,
    IClock clock,
    IOptions<WorkerSettings> options,
    ILogger<WorkItemRetentionHandler> logger) : IWorkHandler<WorkItemRetentionPayload>
{
    public async Task<object?> RunAsync(WorkItemRetentionPayload payload, IWorkProgress progress, CancellationToken ct)
    {
        var cutoff = clock.GetCurrentInstant() - Duration.FromDays(options.Value.WorkItemRetentionDays);
        var deleted = await store.DeleteFinishedBeforeAsync(cutoff, ct);
        Log.ItemsDeleted(logger, deleted);
        return new WorkItemRetentionResult(deleted, cutoff);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} finished work item(s) past retention.")]
        public static partial void ItemsDeleted(ILogger logger, int count);
    }
}

public sealed class WorkItemRetentionSchedule(IOptions<WorkerSettings> options)
    : WorkSchedule<WorkItemRetentionPayload>(
        new WorkItemRetentionPayload(), Duration.FromHours(options.Value.WorkItemRetentionIntervalHours));
