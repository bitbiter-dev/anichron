using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ClassificationMiddleware(
    IngestionClassifier classifier,
    LivePhotoPairer pairer,
    KnownFileReconciler reconciler,
    IngestionSaver saver,
    ILogger<ClassificationMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.Classification;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var classification = await classifier.ClassifyAsync(context, ct);
        context.Classification = classification;
        context.AssetId = classification.Primary.Stored?.Id ?? context.AttemptId;
        if (classification.NeedsProcessing)
        {
            await ReconcileSecondaryAsync(context, classification, ct);
            await next(context, ct);
            return;
        }

        await pairer.PairAsync(context, classification, ct);

        var changed = await reconciler.ReconcileAsync(context, classification.Primary, context.Item.RelativePath, ct);
        if (await ReconcileSecondaryAsync(context, classification, ct))
            changed = true;

        if (!changed)
            return;
        if (classification.RetiredFirst.Count > 0)
        {
            if (!await saver.TrySaveRetiringFirstAsync(context, ct))
                Log.ContentTakenConcurrently(logger, context.Item.RelativePath);

            return;
        }

        await saver.SaveAsync(context, null, ct);
    }

    private async Task<bool> ReconcileSecondaryAsync(
        IngestionContext context, IngestionClassification classification, CancellationToken ct)
        => classification.Secondary is { } secondary
            && await reconciler.ReconcileAsync(context, secondary, context.Item.SecondaryFile!.RelativePath, ct);

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Another consumer took the content now at {RelativePath} first, skipping; the next crawl reconciles it.")]
        public static partial void ContentTakenConcurrently(ILogger logger, string relativePath);
    }
}
