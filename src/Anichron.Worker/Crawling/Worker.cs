using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Scheduling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Crawling;

internal sealed partial class Worker(
    IServiceScopeFactory scopeFactory,
    WorkerState workerState,
    IFileIngestionPipeline ingestionPipeline,
    TimeProvider timeProvider,
    IOptions<WorkerSettings> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly WorkerSettings settings = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Starting(logger, workerState.ResolvedUserId is { } userId
            ? $"dedicated (userId={userId})"
            : "all-user");

        return RecurringWork.RunAsync(
            CrawlAllAsync, TimeSpan.FromHours(settings.CrawlIntervalHours), timeProvider, stoppingToken);
    }

    internal async Task CrawlAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserStorageConfigRepository>();

        var configs = workerState.ResolvedUserId is { } userId
            ? await repository.GetActiveByUserIdAsync(userId, ct)
            : await repository.GetAllActiveAsync(ct);

        foreach (var config in configs)
            await CrawlAsync(config, ct);

        await ReportSharedContentAsync(scope.ServiceProvider, configs, ct);
    }

    private async Task CrawlAsync(UserStorageConfig config, CancellationToken ct)
    {
        Log.CrawlingPath(logger, config.RootPath, config.UserId);
        try
        {
            await ingestionPipeline.RunAsync(config, ct);
        }
        catch (Exception ex) when (!IngestionShutdown.IsInProgress(ex, ct))
        {
            Log.CrawlFailed(logger, config.RootPath, config.UserId, ex);
        }
    }

    private async Task ReportSharedContentAsync(
        IServiceProvider services, IReadOnlyCollection<UserStorageConfig> configs, CancellationToken ct)
    {
        try
        {
            await services.GetRequiredService<ISharedContentReporter>().ReportAsync(configs, ct);
        }
        catch (Exception ex) when (!IngestionShutdown.IsInProgress(ex, ct))
        {
            Log.SharedContentReportFailed(logger, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Worker starting in {Mode} mode.")]
        public static partial void Starting(ILogger logger, string mode);

        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void CrawlingPath(ILogger logger, string rootPath, Guid userId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Crawl of {RootPath} (userId={UserId}) failed.")]
        public static partial void CrawlFailed(ILogger logger, string rootPath, Guid userId, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Reporting duplicate content failed.")]
        public static partial void SharedContentReportFailed(ILogger logger, Exception ex);
    }
}
