using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Configuration;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.IO.Abstractions;
using CrawlingWorker = Anichron.Worker.Crawling.Worker;

namespace Anichron.Worker.Infrastructure;

internal static class WorkerServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        internal IServiceCollection AddWorkerCoreServices()
        {
            services.AddOptions<WorkerSettings>()
                .BindConfiguration("Worker")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddSingleton<IGuidFactory, TimeOrderedGuidFactory>();
            services.AddSingleton<IClock>(SystemClock.Instance);
            services.AddSingleton<WorkerState>();
            return services;
        }

        internal IServiceCollection AddWorkerDataServices(IConfiguration configuration)
        {
            var connectionString = DatabaseConfiguration.GetConnectionString(configuration, new FileSystem());
            services.AddDbContext<AnichronDbContext>(options =>
                options.UseNpgsql(connectionString, o => o.UseNodaTime()));
            services.AddScoped<IUserRepository, EfUserRepository>();
            services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();
            services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
            services.AddScoped<IMediaAssetRepository, EfMediaAssetRepository>();
            services.AddScoped<IDatabaseMigrator, EfDatabaseMigrator>();
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AnichronDbContext>());
            return services;
        }

        internal IServiceCollection AddIngestionServices()
        {
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<ILivePhotoLinker, LivePhotoLinker>();
            services.AddSingleton<IFileIngestionPipeline, FileIngestionPipeline>();
            services.AddSingleton<IProxyDirectoryStrategy, TwoLevelHexShardStrategy>();
            services.AddSingleton<ProxyStagingWriter>();
            services.AddImageProxyServices();
            services.AddVideoProxyServices();
            services.AddScoped<IIngestionMiddleware, LoggingMiddleware>();
            services.AddScoped<IIngestionMiddleware, ContentHashingMiddleware>();
            services.AddScoped<IIngestionMiddleware, IdempotencyCheckMiddleware>();
            services.AddScoped<IIngestionMiddleware, ExifExtractionMiddleware>();
            services.AddScoped<IIngestionMiddleware, ImageProxyMiddleware>();
            services.AddScoped<IIngestionMiddleware, VideoProxyMiddleware>();
            services.AddScoped<IIngestionMiddleware, PersistenceMiddleware>();
            services.AddScoped<IIngestionPipelineRunner, IngestionPipelineRunner>();
            return services;
        }

        // void, not IServiceCollection: these two are private and both call sites above invoke
        // them as statements, so the fluent return was never consumed. The internal members keep
        // returning IServiceCollection because they are part of the chainable surface.
        private void AddVideoProxyServices()
        {
            services.AddSingleton<IProcessLauncher, SystemProcessLauncher>();
            services.AddSingleton<IVideoProcessor, FfmpegVideoProcessor>();
            services.AddSingleton<IVideoProxyGenerator, Video720PGenerator>();
        }

        private void AddImageProxyServices()
        {
            services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
            services.AddSingleton<IImageProxyGenerator, ThumbnailGenerator>();
            services.AddSingleton<IImageProxyGenerator, FullPreviewGenerator>();
            services.AddSingleton<IImageProxyGenerator, BlurhashGenerator>();
        }

        internal IServiceCollection AddWorkerHostedServices()
        {
            services.AddHostedService<DatabaseMigratorService>();
            services.AddHostedService<WorkerInitializer>();
            services.AddHostedService<TokenCleanupService>();
            services.AddHostedService<CrawlingWorker>();
            return services;
        }
    }
}
