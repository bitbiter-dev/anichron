using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class NewContentWriter(
    IMediaAssetRepository repository,
    IngestionSaver saver,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<NewContentWriter> logger) : IOutcomeWriter
{
    public FileOutcome Outcome => FileOutcome.New;

    public async Task<MediaAsset?> TryWriteAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = BuildAsset(context);
        MediaAsset? mov = null;

        if (context.Item.SecondaryFile is not null)
        {
            mov = pairedMovResolver.Resolve(context, context.Exif!.DateCaptured);
            await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
            asset.PairedAssetId = mov.Id;
        }

        repository.Add(asset);
        foreach (var proxy in context.ProxyFilesOf(asset.Id))
            repository.AddProxyFile(proxy);

        if (await saver.TrySaveNewAsync(context, asset, mov?.Id, ct))
            return asset;

        Log.AlreadyPersisted(logger, context.Item.RelativePath);
        return null;
    }

    private MediaAsset BuildAsset(IngestionContext context)
    {
        var relativePath = context.Item.RelativePath;
        var exif = context.Exif!;
        var asset = new MediaAsset
        {
            Id = context.AssetId,
            StorageConfigId = context.Config.Id,
            FilePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            ContentHash = context.ContentHash!,
            MediaType = context.Item.PrimaryMediaType,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
            Metadata = new Metadata
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
        };
        asset.SetCaptureDate(CaptureDate.Resolve(exif.DateCaptured, fileSystem, context.Item.AbsolutePath));
        return asset;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Content of {RelativePath} was persisted concurrently by another consumer, skipping.")]
        public static partial void AlreadyPersisted(ILogger logger, string relativePath);
    }
}
