using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class LivePhotoPairer(
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<LivePhotoPairer> logger)
{
    public Task PairAsync(IngestionContext context, IngestionClassification classification, CancellationToken ct)
        => classification.Pairing switch
        {
            PairingAction.AttachMov => AttachMovAsync(context, classification, ct),
            PairingAction.PointAtMov => Task.FromResult(PointAtMov(context, classification)),
            PairingAction.BackfillMovProxy => pairedMovProxyWriter.WriteMissingAsync(context, classification.Secondary!.Stored!, ct),
            _ => Task.CompletedTask,
        };

    private async Task AttachMovAsync(IngestionContext context, IngestionClassification classification, CancellationToken ct)
        => await pairedMovProxyWriter.WriteMissingAsync(context, PointAtMov(context, classification), ct);

    private MediaAsset PointAtMov(IngestionContext context, IngestionClassification classification)
    {
        var heic = classification.Primary.Row!;
        heic.MediaType = context.Item.PrimaryMediaType;
        var mov = pairedMovResolver.Resolve(context, heic.DateCaptured);
        heic.PairedAssetId = mov.Id;
        Log.Paired(logger, context.Item.RelativePath, heic.Id);
        return mov;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Paired already-ingested {RelativePath} (asset {AssetId}) with its Live Photo video")]
        public static partial void Paired(ILogger logger, string relativePath, Guid assetId);
    }
}
