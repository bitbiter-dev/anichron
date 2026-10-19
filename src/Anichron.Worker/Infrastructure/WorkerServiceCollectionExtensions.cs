using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Infrastructure.Configuration;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Anichron.Worker.Work;
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
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<WorkerState>();
            return services;
        }

        internal IServiceCollection AddWorkerDataServices(IConfiguration configuration)
        {
            var connectionString = DatabaseConfiguration.GetConnectionString(configuration, new FileSystem());
            services.AddDbContextFactory<AnichronDbContext>(options =>
                options.UseNpgsql(connectionString, o => o.UseNodaTime()));
            services.AddScoped<IUserRepository, EfUserRepository>();
            services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();
            services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
            services.AddScoped<IMediaAssetRepository, EfMediaAssetRepository>();
            services.AddScoped<IDatabaseMigrator, EfDatabaseMigrator>();
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AnichronDbContext>());
            services.AddScoped<IWorkQueue, EfWorkQueue>();
            services.AddSingleton<IWorkItemStore, EfWorkItemStore>();
            return services;
        }

        internal IServiceCollection AddWorkQueueServices()
        {
            services.AddSingleton<WorkHandlerRegistry>();
            services.AddWorkHandler<TokenCleanupPayload, TokenCleanupHandler>();
            services.AddWorkHandler<WorkItemRetentionPayload, WorkItemRetentionHandler>();
            services.AddSingleton<IWorkScheduleSource, TokenCleanupSchedule>();
            services.AddSingleton<IWorkScheduleSource, WorkItemRetentionSchedule>();
            return services;
        }

        private void AddWorkHandler<TPayload, THandler>()
            where TPayload : IWorkPayload
            where THandler : class, IWorkHandler<TPayload>
        {
            services.AddScoped<IWorkHandler<TPayload>, THandler>();
            services.AddSingleton(WorkHandlerRegistration.For<TPayload>());
        }

        internal IServiceCollection AddIngestionServices()
        {
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<ILivePhotoLinker, LivePhotoLinker>();
            services.AddSingleton<CrawlExclusions>();
            services.AddSingleton<IFileIngestionPipeline, FileIngestionPipeline>();
            services.AddSingleton<IProxyDirectoryStrategy, TwoLevelHexShardStrategy>();
            services.AddSingleton<ProxyStagingWriter>();
            services.AddImageProxyServices();
            services.AddVideoProxyServices();
            services.AddScoped<PairedMovResolver>();
            services.AddScoped<PairedMovProxyWriter>();
            services.AddScoped<IngestionSaver>();
            services.AddScoped<IngestionClassifier>();
            services.AddScoped<LivePhotoPairer>();
            services.AddScoped<KnownFileReconciler>();
            services.AddScoped<IOutcomeWriter, NewContentWriter>();
            services.AddScoped<IOutcomeWriter, ContentChangedWriter>();
            services.AddScoped<IIngestionMiddleware, LoggingMiddleware>();
            services.AddScoped<IIngestionMiddleware, ContentHashingMiddleware>();
            services.AddScoped<IIngestionMiddleware, ClassificationMiddleware>();
            services.AddScoped<IIngestionMiddleware, ExifExtractionMiddleware>();
            services.AddScoped<IIngestionMiddleware, ImageProxyMiddleware>();
            services.AddScoped<IIngestionMiddleware, VideoProxyMiddleware>();
            services.AddScoped<IIngestionMiddleware, PersistenceMiddleware>();
            services.AddScoped<IIngestionPipelineRunner, IngestionPipelineRunner>();
            return services;
        }

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
            services.AddSingleton<BlurhashEncoder>();
        }

        internal IServiceCollection AddWorkerHostedServices()
        {
            services.AddHostedService<DatabaseMigratorService>();
            services.AddHostedService<WorkerInitializer>();
            services.AddHostedService<WorkScheduler>();
            services.AddHostedService<WorkExecutor>();
            services.AddHostedService<CrawlingWorker>();
            return services;
        }
    }
}
