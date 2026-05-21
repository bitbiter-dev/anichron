using Anichron.Core.Data.Repository;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class IdempotencyCheckMiddleware(
    IMediaAssetRepository repository,
    ILogger<IdempotencyCheckMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.IdempotencyCheck;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first; suppression is safe.
        var existing = await repository.FindByHashAsync(context.ContentHash!, context.Config.Id, ct);
        if (existing is not null)
        {
            Log.AlreadyIngested(logger, context.Item.AbsolutePath);
            return;
        }

        await next(context, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Already ingested {Path}, skipping.")]
        public static partial void AlreadyIngested(ILogger logger, string path);
    }
}
