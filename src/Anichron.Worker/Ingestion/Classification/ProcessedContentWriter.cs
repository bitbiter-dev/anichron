using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal abstract class ProcessedContentWriter(
    IFileSystem fileSystem,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter) : IOutcomeWriter
{
    public abstract IReadOnlyCollection<FileOutcome> Outcomes { get; }

    public async Task<MediaAsset> WriteAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = await WriteAssetAsync(context, ct);
        var classification = context.Classification!;
        if (classification.Secondary is { DefersToAnotherRow: false })
        {
            var mov = await pairedMovResolver.PairAsync(context, asset, context.Exif!.DateCaptured, ct);
            if (classification.DatesMovAfterProcessing)
                mov.DateCaptured = asset.DateCaptured;
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
        var exif = context.Exif!;
        return new AssetContent(
            context.ContentHash!,
            CaptureDate.FromExifOrLastWrite(exif.DateCaptured, fileSystem, context.Item.AbsolutePath),
            context.Item.PrimaryMediaType,
            new Metadata
            {
                Width = exif.Width,
                Height = exif.Height,
                Orientation = exif.Orientation,
                Latitude = exif.Latitude,
                Longitude = exif.Longitude,
                CameraMake = exif.CameraMake,
                CameraModel = exif.CameraModel,
                LensModel = exif.LensModel,
                DurationInSeconds = exif.DurationInSeconds,
                Blurhash = context.Blurhash,
            },
            context.ProxyFilesOf(assetId));
    }
}
