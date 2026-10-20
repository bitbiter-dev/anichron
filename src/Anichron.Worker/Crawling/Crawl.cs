using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Crawling;

public sealed record CrawlSkipped(string Reason);

internal sealed partial class CrawlHandler(
    IUserStorageConfigRepository configs,
    IFileIngestionPipeline pipeline,
    ISharedContentReporter reporter,
    ILogger<CrawlHandler> logger) : IWorkHandler<CrawlPayload>
{
    public async Task<object?> RunAsync(CrawlPayload payload, IWorkProgress progress, CancellationToken ct)
    {
        var config = await configs.FindByIdAsync(payload.StorageConfigId, ct);
        if (config is not { IsActive: true })
        {
            Log.Skipped(logger, payload.StorageConfigId);
            return new CrawlSkipped("The storage config is missing or inactive.");
        }

        Log.Crawling(logger, config.RootPath, config.UserId);
        var counts = await pipeline.RunAsync(config, progress, ct);
        return counts with { Duplicates = await ReportDuplicatesAsync(config, ct) };
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

        [LoggerMessage(Level = LogLevel.Information, Message = "Skipping the crawl of storage config {Id}: it is missing or inactive.")]
        public static partial void Skipped(ILogger logger, Guid id);
    }
}

public sealed class CrawlSchedule(Guid storageConfigId, Duration interval)
    : WorkSchedule<CrawlPayload>(new CrawlPayload(storageConfigId), interval);

public sealed class CrawlSchedules(WorkerState workerState, IOptions<WorkerSettings> options) : IWorkScheduleSource
{
    public async Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(IServiceProvider services, CancellationToken ct)
    {
        var repository = services.GetRequiredService<IUserStorageConfigRepository>();
        var served = workerState.ResolvedUserId is { } userId
            ? await repository.GetActiveByUserIdAsync(userId, ct)
            : await repository.GetAllActiveAsync(ct);
        var interval = Duration.FromHours(options.Value.CrawlIntervalHours);
        return [.. served.Select(config => new CrawlSchedule(config.Id, interval))];
    }
}
