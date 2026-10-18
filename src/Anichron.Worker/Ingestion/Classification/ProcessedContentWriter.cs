using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal abstract partial class ProcessedContentWriter(
    IngestionSaver saver,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger logger) : IOutcomeWriter
{
    public abstract FileOutcome Outcome { get; }

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

    protected void DescribeContent(MediaAsset asset, Metadata metadata, IngestionContext context)
    {
        var exif = context.Exif!;
        asset.ContentHash = context.ContentHash!;
        asset.DateCaptured = CaptureDate.FromExifOrLastWrite(exif.DateCaptured, fileSystem, context.Item.AbsolutePath);
        asset.MediaType = context.Item.PrimaryMediaType;
        asset.LastSeenOnNas = clock.GetCurrentInstant();

        metadata.Width = exif.Width;
        metadata.Height = exif.Height;
        metadata.Orientation = exif.Orientation;
        metadata.Latitude = exif.Latitude;
        metadata.Longitude = exif.Longitude;
        metadata.CameraMake = exif.CameraMake;
        metadata.CameraModel = exif.CameraModel;
        metadata.LensModel = exif.LensModel;
        metadata.DurationInSeconds = exif.DurationInSeconds;
        metadata.Blurhash = context.Blurhash;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Content of {RelativePath} was persisted concurrently by another consumer, skipping.")]
        public static partial void AlreadyPersisted(ILogger logger, string relativePath);
    }
}
