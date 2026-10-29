using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class PersistenceMiddleware(
    IEnumerable<IOutcomeWriter> writers,
    IngestionSaver saver,
    ILogger<PersistenceMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.Persistence;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var outcome = context.Classification!.Primary.Outcome;
        var writer = writers.Single(candidate => candidate.Outcomes.Contains(outcome));
        var asset = await writer.WriteAsync(context, ct);
        if (!await saver.TrySaveItemAsync(context, asset, ct))
        {
            Log.AlreadyPersisted(logger, context.Item.RelativePath);
            return;
        }

        context.Asset = asset;
        Log.Persisted(logger, context.Item.RelativePath, asset.Id);
        await next(context, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Persisted {RelativePath} as asset {AssetId}")]
        public static partial void Persisted(ILogger logger, string relativePath, Guid assetId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Content of {RelativePath} was persisted concurrently by another consumer, skipping.")]
        public static partial void AlreadyPersisted(ILogger logger, string relativePath);
    }
}
