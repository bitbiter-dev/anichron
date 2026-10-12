using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Infrastructure.Data;

namespace Anichron.API.Infrastructure;

public static partial class DatabaseStartupExtensions
{
    extension(WebApplication app)
    {
        public Task MigrateAndSeedDatabaseAsync(CancellationToken ct)
            => new DatabaseStartupRetry(
                    app.Services.GetRequiredService<TimeProvider>(),
                    AppDefaults.Startup.MaxDbRetryAttempts,
                    TimeSpan.FromSeconds(AppDefaults.Startup.DbRetryDelaySeconds))
                .RunAsync(
                    app.MigrateAndSeedOnceAsync,
                    (ex, attempt) => Log.DatabaseNotReady(app.Logger, ex, attempt,
                        AppDefaults.Startup.MaxDbRetryAttempts, AppDefaults.Startup.DbRetryDelaySeconds),
                    ex => Log.DatabaseUnavailable(app.Logger, ex, AppDefaults.Startup.MaxDbRetryAttempts),
                    ct);

        private async Task MigrateAndSeedOnceAsync(CancellationToken ct)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AnichronDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AnichronDbContext>>();

            await dbContext.Database.MigrateWithAdvisoryLockAsync(ct);
            Log.MigrationApplied(logger);

            var bootstrapSeeder = scope.ServiceProvider.GetRequiredService<IBootstrapSeeder>();
            await bootstrapSeeder.SeedAsync(ct);

            var adminResetService = scope.ServiceProvider.GetRequiredService<IBootstrapResetService>();
            await adminResetService.ResetIfRequestedAsync(ct);
        }
    }

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
