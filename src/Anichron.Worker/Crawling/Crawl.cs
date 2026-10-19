using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Crawling;

public sealed record CrawlSkipped(string Reason);

internal sealed partial class CrawlHandler(
    IUserStorageConfigRepository configs,
    IFileIngestionPipeline pipeline,
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
        return await pipeline.RunAsync(config, progress, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void Crawling(ILogger logger, string rootPath, Guid userId);

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
