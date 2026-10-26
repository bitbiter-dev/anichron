using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Work;

namespace Anichron.Worker.Crawling;

internal sealed partial class CrawlHandler(
    ActiveStorageConfigs activeConfigs,
    IUserStorageConfigRepository configs,
    IFileIngestionPipeline pipeline,
    IMissingFileSweep sweep,
    ISharedContentReporter reporter,
    IWorkQueue queue,
    ILogger<CrawlHandler> logger) : IWorkHandler<CrawlPayload>
{
    public async Task<object?> RunAsync(CrawlPayload payload, IWorkProgress progress, CancellationToken ct)
    {
        if (await activeConfigs.FindAsync(CrawlPayload.Kind, payload.StorageConfigId, ct) is not { } config)
            return StorageConfigSkipped.MissingOrInactive;

        Log.Crawling(logger, config.RootPath, config.UserId);
        var run = await pipeline.RunAsync(config, progress, ct);
        var reconciliation = await ReconcileAsync(config, run, ct);
        var result = run.Result with
        {
            Duplicates = await ReportDuplicatesAsync(config, run.DuplicateCopies, ct),
            Reconciliation = reconciliation,
        };
        await queue.EnqueueAsync(new BurstDetectionPayload(config.Id), null, ct);
        return result;
    }

    private async Task<ReconciliationCounts> ReconcileAsync(UserStorageConfig config, CrawlRun run, CancellationToken ct)
    {
        var missing = await sweep.SweepAsync(config, run, ct);
        var counts = new ReconciliationCounts(run.SoftDeleted + missing.SoftDeleted, run.AutoRestored, missing.SkippedBecause);
        if (counts.SkippedBecause is { } reason)
            Log.ReconciledWithoutSweep(logger, config.Id, counts.SoftDeleted, counts.AutoRestored, reason);
        else
            Log.Reconciled(logger, config.Id, counts.SoftDeleted, counts.AutoRestored);
        return counts;
    }

    private async Task<DuplicateCounts?> ReportDuplicatesAsync(
        UserStorageConfig config, int copiesWithin, CancellationToken ct)
    {
        try
        {
            var usersConfigs = await configs.GetActiveByUserIdAsync(config.UserId, ct);
            return await reporter.ReportAsync(config, usersConfigs, copiesWithin, ct);
        }
        catch (Exception ex) when (!IngestionCancellation.IsRequested(ex, ct))
        {
            Log.DuplicateReportFailed(logger, config.Id, ex);
            return null;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void Crawling(ILogger logger, string rootPath, Guid userId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Reconciled storage config {Id}: {SoftDeleted} soft-deleted, {AutoRestored} auto-restored.")]
        public static partial void Reconciled(ILogger logger, Guid id, int softDeleted, int autoRestored);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Reconciled storage config {Id}: {SoftDeleted} soft-deleted, {AutoRestored} auto-restored; "
                + "missing files were not looked for ({Reason}).")]
        public static partial void ReconciledWithoutSweep(ILogger logger, Guid id, int softDeleted, int autoRestored, MissingFileSkip reason);

        [LoggerMessage(Level = LogLevel.Error, Message = "Reporting duplicate content for storage config {Id} failed.")]
        public static partial void DuplicateReportFailed(ILogger logger, Guid id, Exception ex);
    }
}
