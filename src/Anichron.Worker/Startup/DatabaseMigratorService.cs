using Anichron.Core.Data;
using Anichron.Infrastructure.Data;

namespace Anichron.Worker.Startup;

public sealed partial class DatabaseMigratorService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigratorService> logger) : IHostedService
{
    private const int MaxAttempts = 10;
    private const int RetryDelaySeconds = 5;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AnichronDbContext>();
                await db.Database.MigrateWithAdvisoryLockAsync(cancellationToken);
                Log.MigrationApplied(logger);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                Log.DatabaseNotReady(logger, ex, attempt, MaxAttempts, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                Log.DatabaseUnavailable(logger, ex, MaxAttempts);
                throw new InvalidOperationException(
                    $"Database unavailable after {MaxAttempts} attempts. Aborting.",
                    ex);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Database migration applied successfully.")]
        public static partial void MigrationApplied(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Database not ready (attempt {Attempt}/{Max}). Retrying in {Delay}s.")]
        public static partial void DatabaseNotReady(ILogger logger, Exception ex, int attempt, int max, int delay);

        [LoggerMessage(Level = LogLevel.Error, Message = "Database unavailable after {Max} attempts. Aborting.")]
        public static partial void DatabaseUnavailable(ILogger logger, Exception ex, int max);
    }
}
