using Anichron.Worker.Ingestion.Middlewares;

namespace Anichron.Worker.Ingestion.Pipeline;

internal static class IngestionServiceCollectionExtensions
{
    internal static IServiceCollection AddIngestionSteps(this IServiceCollection services)
    {
        services.AddScoped<IIngestionMiddleware, LoggingMiddleware>();
        services.AddScoped<IIngestionMiddleware, ContentHashingMiddleware>();
        services.AddScoped<IIngestionMiddleware, IdempotencyCheckMiddleware>();
        services.AddScoped<IIngestionMiddleware, ExifExtractionMiddleware>();
        services.AddScoped<IIngestionPipelineRunner, IngestionPipelineRunner>();
        return services;
    }
}
