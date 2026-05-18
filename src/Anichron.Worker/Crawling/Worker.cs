using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Crawling;

internal sealed partial class Worker(
    IServiceScopeFactory scopeFactory,
    WorkerState workerState,
    IFileIngestionPipeline ingestionPipeline,
    IOptions<WorkerSettings> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly WorkerSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = workerState.ResolvedUserId is { } userId
            ? $"dedicated (userId={userId})"
            : "all-user";
        Log.Starting(logger, mode);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CrawlAllAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(_settings.CrawlIntervalHours), stoppingToken);
        }
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

    private async Task CrawlAsync(UserStorageConfig config, CancellationToken ct)
    {
        Log.CrawlingPath(logger, config.RootPath, config.UserId);
        await ingestionPipeline.RunAsync(config, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Worker starting in {Mode} mode.")]
        public static partial void Starting(ILogger logger, string mode);

        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void CrawlingPath(ILogger logger, string rootPath, Guid userId);
    }
}
