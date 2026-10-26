using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal abstract partial class ProcessedContentWriter(
    IngestionSaver saver,
    IFileSystem fileSystem,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger logger) : IOutcomeWriter
{
    public abstract IReadOnlyCollection<FileOutcome> Outcomes { get; }

    public async Task<MediaAsset?> TryWriteAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = await WriteAssetAsync(context, ct);
        MediaAsset? mov = null;

        if (context.Item.SecondaryFile is not null)
        {
            mov = pairedMovResolver.Resolve(context, context.Exif!.DateCaptured);
            await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
            asset.PairedAssetId = mov.Id;
        }

        if (await saver.TrySaveNewAsync(context, asset, mov?.Id, ct))
            return asset;

        Log.AlreadyPersisted(logger, context.Item.RelativePath);
        return null;
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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Content of {RelativePath} was persisted concurrently by another consumer, skipping.")]
        public static partial void AlreadyPersisted(ILogger logger, string relativePath);
    }
}
