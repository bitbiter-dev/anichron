using Anichron.Core.Work;
using Anichron.Worker.Crawling;
using Anichron.Worker.Scheduling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Work;

public sealed partial class WorkExecutor(
    IWorkItemStore store,
    WorkHandlerRegistry registry,
    WorkerState workerState,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<WorkerSettings> options,
    ILogger<WorkExecutor> logger) : BackgroundService
{
    private readonly WorkerSettings settings = options.Value;
    private readonly string claimant = ClaimantName(options.Value, Environment.MachineName);

    internal static string ClaimantName(WorkerSettings settings, string host)
        => $"{(settings.User.Length == 0 ? "all" : settings.User)}@{host}";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => RecurringWork.RunAsync(
            DrainOrLogAsync, TimeSpan.FromSeconds(settings.WorkPollIntervalSeconds), timeProvider, stoppingToken);

    private Duration Lease => Duration.FromSeconds(settings.WorkLeaseSeconds);

    private Instant Now => Instant.FromDateTimeOffset(timeProvider.GetUtcNow());

    private async Task DrainOrLogAsync(CancellationToken stopping)
    {
        try
        {
            while (await store.ClaimAsync(claimant, workerState.ResolvedUserId, Now, Lease, stopping) is { } work)
                await RunAsync(work, stopping);
        }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            Log.QueueFailed(logger, ex);
        }
    }

    private async Task RunAsync(ClaimedWork work, CancellationToken stopping)
    {
        using var running = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        using var done = new CancellationTokenSource();
        var progress = new WorkProgressSink();
        await using var scope = scopeFactory.CreateAsyncScope();
        var heartbeat = HeartbeatAsync(work.Attempt, progress, running, done.Token);

        object? result = null;
        Exception? failure = null;
        try
        {
            result = await Task.Run(
                () => registry.RunAsync(work, scope.ServiceProvider, progress, running.Token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await done.CancelAsync();
        if (await heartbeat)
        {
            Log.Fenced(logger, work.Attempt.Id);
        }
        else if (failure is null)
        {
            await store.SucceedAsync(
                work.Attempt, progress.Snapshot, WorkJson.SerializeResult(result), Now, CancellationToken.None);
        }
        else if (failure is OperationCanceledException && stopping.IsCancellationRequested)
        {
            await store.ReleaseAsync(work.Attempt, Now, CancellationToken.None);
            Log.Released(logger, work.Attempt.Id);
            stopping.ThrowIfCancellationRequested();
        }
        else
        {
            await store.FailAsync(
                work.Attempt, progress.Snapshot, $"{failure.GetType().Name}: {failure.Message}", Now, CancellationToken.None);
            Log.Failed(logger, work.Attempt.Id, work.Kind, failure);
        }
    }

    private async Task<bool> HeartbeatAsync(
        WorkAttempt attempt, WorkProgressSink progress, CancellationTokenSource running, CancellationToken done)
    {
        var interval = TimeSpan.FromSeconds(settings.WorkHeartbeatSeconds);
        try
        {
            while (true)
            {
                await Task.Delay(interval, timeProvider, done);
                try
                {
                    if (!await store.HeartbeatAsync(attempt, progress.Snapshot, Now, Lease, CancellationToken.None))
                    {
                        await running.CancelAsync();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.HeartbeatFailed(logger, attempt.Id, ex);
                }
            }
        }
        catch (OperationCanceledException) when (done.IsCancellationRequested)
        {
            return false;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Claiming or finishing work failed; will retry at the next poll.")]
        public static partial void QueueFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Work item {Id} ({Kind}) failed.")]
        public static partial void Failed(ILogger logger, Guid id, Core.Domain.WorkKind kind, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Work item {Id} was handed back to the queue at shutdown.")]
        public static partial void Released(ILogger logger, Guid id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Work item {Id} was recovered by another worker; this attempt stopped and wrote nothing more.")]
        public static partial void Fenced(ILogger logger, Guid id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat for work item {Id} failed; will retry.")]
        public static partial void HeartbeatFailed(ILogger logger, Guid id, Exception ex);
    }
}
