using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Anichron.API.Infrastructure;

public static class ApplicationExtensions
{
    extension(WebApplication app)
    {
        public WebApplication MapApiEndpoints()
        {
            var api = app.MapGroup("/api/v1");
            api.MapAuthEndpoints();
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
                    var db = scope.ServiceProvider.GetRequiredService<AnichronDbContext>();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<AnichronDbContext>>();

                    await db.Database.MigrateAsync(ct);
                    logger.LogInformation("Database migration applied successfully.");

                    var bootstrapSeeder = scope.ServiceProvider.GetRequiredService<IBootstrapSeeder>();
                    await bootstrapSeeder.SeedAsync(ct);

                    var adminResetService = scope.ServiceProvider.GetRequiredService<IAdminResetService>();
                    await adminResetService.ResetIfRequestedAsync(ct);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    app.Logger.LogWarning(ex,
                        "Database not ready (attempt {Attempt}/{Max}). Retrying in {Delay}s.",
                        attempt, maxAttempts, retryDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), ct);
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex,
                        "Database unavailable after {Max} attempts. Aborting.",
                        AppDefaults.Startup.MaxDbRetryAttempts);
                    throw new InvalidOperationException("Database unavailable after {Max} attempts. Aborting.", ex);
                }
            }
        }
    }
}
