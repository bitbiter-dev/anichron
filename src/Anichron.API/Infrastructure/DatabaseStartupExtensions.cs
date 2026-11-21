using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Infrastructure.Configuration;
using Anichron.Infrastructure.Data;

namespace Anichron.API.Infrastructure;

public static partial class DatabaseStartupExtensions
{
    public const string MigrateCommand = "migrate";

    public static async Task MigrateAndSeedAsync(string[] args)
    {
        using var hostSettings = new ConfigurationManager();
        hostSettings.AddEnvironmentVariables("ASPNETCORE_");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, Configuration = hostSettings });
        builder.AddAppConfiguration(ApiIniDefaults.Entries);
        builder.Services.AddDatabase(builder.Configuration).AddBootstrapAdmin(builder.Configuration);

        using var host = builder.Build();
        await host.StartAsync();
        await host.MigrateAndSeedDatabaseAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        await host.StopAsync();
    }

    extension(IHost host)
    {
        private async Task MigrateAndSeedDatabaseAsync(CancellationToken ct)
        {
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseStartupExtensions));
            await new DatabaseStartupRetry(
                    TimeProvider.System,
                    AppDefaults.Startup.MaxDbRetryAttempts,
                    TimeSpan.FromSeconds(AppDefaults.Startup.DbRetryDelaySeconds))
                .RunAsync(
                    host.MigrateOnceAsync,
                    (ex, attempt) => Log.DatabaseNotReady(logger, ex, attempt,
                        AppDefaults.Startup.MaxDbRetryAttempts, AppDefaults.Startup.DbRetryDelaySeconds),
                    ex => Log.DatabaseUnavailable(logger, ex, AppDefaults.Startup.MaxDbRetryAttempts),
                    ct);
            await host.SeedAsync(ct);
        }

        private async Task MigrateOnceAsync(CancellationToken ct)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AnichronDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AnichronDbContext>>();

            await dbContext.Database.MigrateWithAdvisoryLockAsync(ct);
            Log.MigrationApplied(logger);
        }

        private async Task SeedAsync(CancellationToken ct)
        {
            await using var scope = host.Services.CreateAsyncScope();
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
