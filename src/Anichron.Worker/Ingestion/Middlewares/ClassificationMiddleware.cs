using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed class ClassificationMiddleware(
    IngestionClassifier classifier,
    LivePhotoPairer pairer,
    KnownFileReconciler reconciler,
    IngestionSaver saver) : IIngestionMiddleware
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
            await next(context, ct);
            return;
        }

        await pairer.PairAsync(context, classification, ct);

        var changed = reconciler.Reconcile(classification.Primary, context.Item.RelativePath);
        if (classification.Secondary is not null
            && reconciler.Reconcile(classification.Secondary, context.Item.SecondaryFile!.RelativePath))
        {
            changed = true;
        }

        if (changed)
            await saver.SaveAsync(context, null, ct);
    }
}
