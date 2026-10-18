using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class PersistenceMiddleware(
    IEnumerable<IAssetWriter> writers,
    ILogger<PersistenceMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.Persistence;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ClassificationMiddleware ran first; suppression is safe.
        var outcome = context.Classification!.Primary.Outcome;
        var writer = writers.Single(w => w.Writes == outcome);
        var asset = await writer.TryWriteAsync(context, ct);
        if (asset is null)
            return;

        context.Asset = asset;
        Log.Persisted(logger, context.Item.RelativePath, asset.Id);
        await next(context, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Persisted {RelativePath} as asset {AssetId}")]
        public static partial void Persisted(ILogger logger, string relativePath, Guid assetId);
    }
}
