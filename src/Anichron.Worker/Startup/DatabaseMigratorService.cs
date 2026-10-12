using Anichron.Infrastructure.Data;
using Anichron.Worker.Settings;

namespace Anichron.Worker.Startup;

public sealed partial class DatabaseMigratorService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DatabaseMigratorService> logger) : IHostedService
{
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(WorkerDefaults.Migrator.RetryDelaySeconds);

    public Task StartAsync(CancellationToken cancellationToken)
        => new DatabaseStartupRetry(timeProvider, WorkerDefaults.Migrator.MaxAttempts, RetryDelay).RunAsync(
            MigrateAsync,
            (ex, attempt) => Log.DatabaseNotReady(logger, ex, attempt, WorkerDefaults.Migrator.MaxAttempts, (int)RetryDelay.TotalSeconds),
            ex => Log.DatabaseUnavailable(logger, ex, WorkerDefaults.Migrator.MaxAttempts),
            cancellationToken);

    private async Task MigrateAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(ct);
        Log.MigrationApplied(logger);
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
