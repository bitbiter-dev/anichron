using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class LivePhotoPairer(
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    IngestionSaver saver,
    ILogger<LivePhotoPairer> logger)
{
    public Task PairAsync(IngestionContext context, IngestionClassification classification, CancellationToken ct)
        => classification.Pairing switch
        {
            PairingAction.AttachMov => AttachMovAsync(context, classification, ct),
            PairingAction.BackfillMovProxy => BackfillMovProxyAsync(context, classification, ct),
            _ => Task.CompletedTask,
        };

    private async Task AttachMovAsync(IngestionContext context, IngestionClassification classification, CancellationToken ct)
    {
        var heic = classification.Primary.Row!;
        heic.MediaType = context.Item.PrimaryMediaType;
        var mov = pairedMovResolver.Resolve(context, heic.DateCaptured);
        heic.PairedAssetId = mov.Id;
        if (classification.Secondary!.TakesContent)
            return;

        await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
        if (classification.RetiredFirst.Count > 0)
            return;

        await saver.SaveAsync(context, mov.Id, ct);
        Log.Paired(logger, context.Item.RelativePath, heic.Id);
    }

    private async Task BackfillMovProxyAsync(IngestionContext context, IngestionClassification classification, CancellationToken ct)
    {
        var mov = classification.Secondary!.Stored!;
        if (await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct))
            await saver.SaveAsync(context, mov.Id, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Paired already-ingested {RelativePath} (asset {AssetId}) with its Live Photo video")]
        public static partial void Paired(ILogger logger, string relativePath, Guid assetId);
    }
}
