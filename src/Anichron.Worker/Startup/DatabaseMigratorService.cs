using Anichron.Worker.Settings;

namespace Anichron.Worker.Startup;

public sealed partial class DatabaseMigratorService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigratorService> logger) : IHostedService
{
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(WorkerDefaults.Migrator.RetryDelaySeconds);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= WorkerDefaults.Migrator.MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
                await migrator.MigrateAsync(cancellationToken);
                Log.MigrationApplied(logger);
                return;
            }
            catch (Exception ex) when (attempt < WorkerDefaults.Migrator.MaxAttempts)
            {
                Log.DatabaseNotReady(logger, ex, attempt, WorkerDefaults.Migrator.MaxAttempts, (int)RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.DatabaseUnavailable(logger, ex, WorkerDefaults.Migrator.MaxAttempts);
                throw new InvalidOperationException(
                    $"Database unavailable after {WorkerDefaults.Migrator.MaxAttempts} attempts. Aborting.",
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
