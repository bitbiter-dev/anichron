using Anichron.Core.Data.Repository;
using Anichron.Worker.Scheduling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Maintenance;

public sealed partial class TokenCleanupService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    TimeProvider timeProvider,
    IOptions<WorkerSettings> options,
    ILogger<TokenCleanupService> logger) : BackgroundService
{
    private readonly WorkerSettings settings = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => RecurringWork.RunAsync(
            CleanUpOrLogAsync, TimeSpan.FromHours(settings.TokenCleanupIntervalHours), timeProvider, stoppingToken);

    private async Task CleanUpOrLogAsync(CancellationToken ct)
    {
        try
        {
            await RunCleanupAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.CleanupFailed(logger, ex);
        }
    }

    internal async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var now = clock.GetCurrentInstant();
        var deleted = await repository.DeleteExpiredAsync(now, ct);
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
