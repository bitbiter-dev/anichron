using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Configuration;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.IO.Abstractions;
using CrawlingWorker = Anichron.Worker.Crawling.Worker;

namespace Anichron.Worker.Infrastructure;

public static class HostApplicationBuilderExtensions
{
    extension(HostApplicationBuilder builder)
    {
        public HostApplicationBuilder AddAppConfiguration()
        {
            var iniPath = Path.Combine(builder.Environment.ContentRootPath, "configuration", "app.ini");
            new AppIniInitializer(new FileSystem()).EnsureUpToDate(iniPath,
            [
                new IniEntry("Worker", "User", () => string.Empty),
                new IniEntry("Worker", "RootPath", () => "/data/originals"),
                new IniEntry("Worker", "CrawlIntervalHours", () => "4"),
                new IniEntry("Worker", "MaxConcurrentFiles", () => "4"),
                new IniEntry("Worker", "TokenCleanupIntervalHours", () => "24"),
                new IniEntry("Worker", "ProxyPath", () => "/data/proxies"),
            ]);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }

        public HostApplicationBuilder AddIngestionServices()
        {
            builder.Services.AddSingleton<IFileSystem, FileSystem>();
            builder.Services.AddSingleton<ILivePhotoLinker, LivePhotoLinker>();
            builder.Services.AddSingleton<IFileIngestionPipeline, FileIngestionPipeline>();
            builder.Services.AddScoped<IMediaAssetRepository, EfMediaAssetRepository>();
            builder.Services.AddIngestionSteps();
            return builder;
        }

        public HostApplicationBuilder AddWorkerCoreServices()
        {
            builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"));
            builder.Services.AddSingleton<IClock>(SystemClock.Instance);
            builder.Services.AddSingleton<WorkerState>();
            return builder;
        }

        public HostApplicationBuilder AddWorkerDataServices()
        {
            var connectionString = DatabaseConfiguration.GetConnectionString(builder.Configuration, new FileSystem());
            builder.Services.AddDbContext<AnichronDbContext>(options =>
                options.UseNpgsql(connectionString, o => o.UseNodaTime()));
            builder.Services.AddScoped<IUserRepository, EfUserRepository>();
            builder.Services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();
            builder.Services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
            builder.Services.AddScoped<IDatabaseMigrator, EfDatabaseMigrator>();
            return builder;
        }

        public HostApplicationBuilder AddWorkerHostedServices()
        {
            builder.Services.AddHostedService<DatabaseMigratorService>();
            builder.Services.AddHostedService<WorkerInitializer>();
            builder.Services.AddHostedService<TokenCleanupService>();
            builder.Services.AddHostedService<CrawlingWorker>();
            return builder;
        }
    }
}
