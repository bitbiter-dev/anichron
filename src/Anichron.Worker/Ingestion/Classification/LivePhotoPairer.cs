using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class LivePhotoPairer(
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<LivePhotoPairer> logger)
{
    public async Task PairAsync(IngestionContext context, IngestionClassification classification, CancellationToken ct)
    {
        if (classification.Pairing is PairingAction.AttachMov or PairingAction.PointAtMov)
        {
            var mov = PointPhotoAtMov(context, classification);
            if (classification.Pairing == PairingAction.AttachMov)
                await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
        }
        else if (classification.Pairing == PairingAction.BackfillMovProxy)
        {
            await pairedMovProxyWriter.WriteMissingAsync(context, classification.Secondary!.Stored!, ct);
        }
    }

    private MediaAsset PointPhotoAtMov(IngestionContext context, IngestionClassification classification)
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
