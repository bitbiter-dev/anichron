using Anichron.Worker.Ingestion.Middlewares;

namespace Anichron.Worker.Ingestion.Pipeline;

internal static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionSteps(this IServiceCollection services)
    {
        services.AddSingleton<IIngestionMiddleware, LoggingMiddleware>();
        services.AddSingleton<IIngestionMiddleware, ContentHashingMiddleware>();
        services.AddSingleton<IIngestionMiddleware, IdempotencyCheckMiddleware>();
        services.AddSingleton<IIngestionMiddleware, ExifExtractionMiddleware>();
        return services;
    }
}
