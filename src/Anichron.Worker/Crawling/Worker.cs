using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
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
    }

    private Task CrawlAsync(UserStorageConfig config, CancellationToken ct)
    {
        Log.CrawlingPath(logger, config.RootPath, config.UserId);
        return ingestionPipeline.RunAsync(config, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Worker starting in {Mode} mode.")]
        public static partial void Starting(ILogger logger, string mode);

        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void CrawlingPath(ILogger logger, string rootPath, Guid userId);
    }
}
