using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker;

public sealed partial class Worker(
    IServiceScopeFactory scopeFactory,
    WorkerState workerState,
    IOptions<WorkerSettings> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly WorkerSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = workerState.ResolvedUserId is { } uid
            ? $"dedicated (userId={uid})"
            : "all-user";
        Log.Starting(logger, mode);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CrawlAllAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(_settings.CrawlIntervalHours), stoppingToken);
        }
    }

    private async Task CrawlAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserStorageConfigRepository>();

        var configs = workerState.ResolvedUserId is { } userId
            ? await repo.GetActiveByUserIdAsync(userId, ct)
            : await repo.GetAllActiveAsync(ct);

        foreach (var config in configs)
            await CrawlAsync(config, ct);
    }

    private Task CrawlAsync(UserStorageConfig config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Log.CrawlingPath(logger, config.RootPath, config.UserId);
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Worker starting in {Mode} mode.")]
        public static partial void Starting(ILogger logger, string mode);

        [LoggerMessage(Level = LogLevel.Information, Message = "Crawling {RootPath} (userId={UserId}).")]
        public static partial void CrawlingPath(ILogger logger, string rootPath, Guid userId);
    }
}
