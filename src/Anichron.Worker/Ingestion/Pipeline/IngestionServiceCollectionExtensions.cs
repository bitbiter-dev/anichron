using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Ingestion.Pipeline;

internal static class IngestionServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        internal IServiceCollection AddIngestionSteps()
        {
            services.AddImageProxyServices();
            services.AddScoped<IIngestionMiddleware, LoggingMiddleware>();
            services.AddScoped<IIngestionMiddleware, ContentHashingMiddleware>();
            services.AddScoped<IIngestionMiddleware, IdempotencyCheckMiddleware>();
            services.AddScoped<IIngestionMiddleware, ExifExtractionMiddleware>();
            services.AddScoped<IIngestionMiddleware, ImageProxyMiddleware>();
            services.AddScoped<IIngestionMiddleware, PersistenceMiddleware>();
            services.AddScoped<IIngestionPipelineRunner, IngestionPipelineRunner>();
            return services;
        }

        private IServiceCollection AddImageProxyServices()
        {
            services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
            services.AddSingleton<IProxyDirectoryStrategy, TwoLevelHexShardStrategy>();
            services.AddSingleton<IImageProxyGenerator, ThumbnailGenerator>();
            services.AddSingleton<IImageProxyGenerator, FullPreviewGenerator>();
            services.AddSingleton<IImageProxyGenerator, BlurhashGenerator>();
            return services;
        }
    }
}
