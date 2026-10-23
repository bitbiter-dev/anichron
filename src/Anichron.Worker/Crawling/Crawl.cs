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
    ISharedContentReporter reporter,
    IWorkQueue queue,
    ILogger<CrawlHandler> logger) : IWorkHandler<CrawlPayload>
{
    public async Task<object?> RunAsync(CrawlPayload payload, IWorkProgress progress, CancellationToken ct)
    {
        if (await activeConfigs.FindAsync(CrawlPayload.Kind, payload.StorageConfigId, ct) is not { } config)
            return StorageConfigSkipped.MissingOrInactive;

        Log.Crawling(logger, config.RootPath, config.UserId);
        var counts = await pipeline.RunAsync(config, progress, ct);
        var result = counts with { Duplicates = await ReportDuplicatesAsync(config, ct) };
        await queue.EnqueueAsync(new BurstDetectionPayload(config.Id), null, ct);
        return result;
    }

    private async Task<DuplicateCounts?> ReportDuplicatesAsync(UserStorageConfig config, CancellationToken ct)
    {
        try
        {
            var usersConfigs = await configs.GetActiveByUserIdAsync(config.UserId, ct);
            return await reporter.ReportAsync(config, usersConfigs, ct);
        }
        catch (Exception ex) when (!IngestionShutdown.IsInProgress(ex, ct))
        {
            Log.DuplicateReportFailed(logger, config.Id, ex);
            return null;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void Crawling(ILogger logger, string rootPath, Guid userId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Reporting duplicate content for storage config {Id} failed.")]
        public static partial void DuplicateReportFailed(ILogger logger, Guid id, Exception ex);
    }
}
