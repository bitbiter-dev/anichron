using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class PersistenceMiddleware(
    IMediaAssetRepository repository,
    IUnitOfWork unitOfWork,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<PersistenceMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.Persistence;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var asset = BuildAsset(context);

        if (context.Item.SecondaryFile is { } secondary)
        {
            // Ordering guarantees ContentHashingMiddleware ran first and set SecondaryHash; suppression is safe.
            var secondaryAsset = BuildSecondaryAsset(secondary, context.SecondaryHash!, context);
            asset.PairedAssetId = secondaryAsset.Id;
            repository.Add(secondaryAsset);
        }

        repository.Add(asset);
        await unitOfWork.SaveChangesAsync(ct);
        context.Asset = asset;
        Log.Persisted(logger, context.Item.RelativePath, asset.Id);
        await next(context, ct);
    }

    private MediaAsset BuildAsset(IngestionContext context)
    {
        var relativePath = context.Item.RelativePath;
        var mediaType = context.Item.PrimaryMediaType;
        var exif = context.Exif!;
        var dateCaptured = exif.DateCaptured ?? FallbackDate(context.Item.AbsolutePath);

        return new MediaAsset
        {
            Id = context.AssetId,
            StorageConfigId = context.Config.Id,
            FilePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            ContentHash = context.ContentHash!,
            DateCaptured = dateCaptured,
            Month = dateCaptured.Month,
            Day = dateCaptured.Day,
            Year = dateCaptured.Year,
            MediaType = mediaType,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
            ProxyFiles = [.. context.ProxyFiles],
            Metadata = new Metadata
            {
                Width = exif.Width,
                Height = exif.Height,
                OrientationDegrees = exif.OrientationDegrees,
                Latitude = exif.Latitude,
                Longitude = exif.Longitude,
                CameraMake = exif.CameraMake,
                CameraModel = exif.CameraModel,
                LensModel = exif.LensModel,
                DurationInSeconds = exif.DurationInSeconds,
            },
        };
    }

    private MediaAsset BuildSecondaryAsset(SecondaryFileDescriptor secondary, string hash, IngestionContext context)
    {
        var exif = context.Exif!;
        var dateCaptured = exif.DateCaptured ?? FallbackDate(secondary.AbsolutePath);

        return new MediaAsset
        {
            Id = guidFactory.NewGuid(),
            StorageConfigId = context.Config.Id,
            FilePath = secondary.RelativePath,
            FileName = Path.GetFileName(secondary.RelativePath),
            ContentHash = hash,
            DateCaptured = dateCaptured,
            Month = dateCaptured.Month,
            Day = dateCaptured.Day,
            Year = dateCaptured.Year,
            MediaType = secondary.MediaType,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
        };
    }

    private LocalDateTime FallbackDate(string absolutePath)
    {
        var lastWrite = fileSystem.FileInfo.New(absolutePath).LastWriteTime;
        return LocalDateTime.FromDateTime(lastWrite);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Persisted {RelativePath} as asset {AssetId}")]
        public static partial void Persisted(ILogger logger, string relativePath, Guid assetId);
    }
}
