using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Anichron.API.Infrastructure;

public static partial class ApplicationExtensions
{
    extension(WebApplication app)
    {
        public WebApplication MapApiEndpoints()
        {
            app.MapHealthChecks(ApiPaths.Healthz, new HealthCheckOptions
            {
                ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
            }).AllowAnonymous();

            var api = app.MapGroup(ApiPaths.Base);
            api.MapAuthEndpoints();
            api.MapUserEndpoints();
            api.MapAdminEndpoints();
            return app;
        }

        public async Task MigrateAndSeedDatabaseAsync(CancellationToken ct)
        {
            const int maxAttempts = AppDefaults.Startup.MaxDbRetryAttempts;
            const int retryDelaySeconds = AppDefaults.Startup.DbRetryDelaySeconds;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
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
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    Log.DatabaseNotReady(app.Logger, ex, attempt, maxAttempts, retryDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), ct);
                }
                catch (Exception ex)
                {
                    Log.DatabaseUnavailable(app.Logger, ex, AppDefaults.Startup.MaxDbRetryAttempts);
                    throw new InvalidOperationException(
                        $"Database unavailable after {AppDefaults.Startup.MaxDbRetryAttempts} attempts. Aborting.",
                        ex);
                }
            }
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
