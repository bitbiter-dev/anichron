using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Ingestion.Classification;

internal abstract class ProcessedContentWriter(
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    CaptureZones captureZones) : IOutcomeWriter
{
    public abstract IReadOnlyCollection<FileOutcome> Outcomes { get; }

    public async Task<MediaAsset> WriteAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = await WriteAssetAsync(context, ct);
        var classification = context.Classification!;
        if (classification.Secondary is { DefersToAnotherRow: false })
        {
            var mov = await pairedMovResolver.PairAsync(context, asset, ct);
            await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
        }

        return asset;
    }

    protected abstract Task<MediaAsset> WriteAssetAsync(IngestionContext context, CancellationToken ct);

    /// <summary>The processed file's content, with the proxies this attempt registered for <paramref name="assetId"/>.</summary>
    /// <param name="context">The processed item.</param>
    /// <param name="assetId">The row that takes the content.</param>
    protected AssetContent ContentOf(IngestionContext context, Guid assetId)
    {
        var extracted = context.Extracted!;
        return new AssetContent(
            context.ContentHash!,
            captureZones.DateOf(extracted),
            context.Item.PrimaryMediaType,
            extracted.Description,
            context.Blurhash,
            context.ProxyFilesOf(assetId));
    }
}
