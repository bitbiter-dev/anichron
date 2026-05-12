using Anichron.Core.Data.Repository;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker;

public sealed partial class TokenCleanupService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<WorkerSettings> options,
    ILogger<TokenCleanupService> logger) : BackgroundService
{
    private readonly WorkerSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.TokenCleanupIntervalHours));
        do
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.CleanupFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var now = clock.GetCurrentInstant();
        var deleted = await repo.DeleteExpiredAsync(now, ct);
        Log.TokensDeleted(logger, deleted);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired refresh token(s).")]
        public static partial void TokensDeleted(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Error, Message = "Token cleanup failed; will retry on next tick.")]
        public static partial void CleanupFailed(ILogger logger, Exception ex);
    }
}
