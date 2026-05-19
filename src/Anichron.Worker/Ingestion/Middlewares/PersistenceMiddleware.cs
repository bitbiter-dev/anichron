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
    ILogger<PersistenceMiddleware> logger) : IIngestionMiddleware
{
    public bool CanInvoke(IngestionContext context)
        => context.ContentHash is not null
           && context.Exif is not null
           && (context.Item is not LivePhotoPairItem || context.MovContentHash is not null);

    public IngestionStepError OnCannotInvoke(IngestionContext context)
        => new("ContentHash and Exif must be set before persistence");

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var asset = BuildAsset(context);

        if (context.Item is LivePhotoPairItem livePhoto)
        {
            var movAsset = BuildMovAsset(livePhoto, context);
            asset.LivePhotoPairId = movAsset.Id;
            repository.Add(movAsset);
        }

        repository.Add(asset);
        await unitOfWork.SaveChangesAsync(ct);
        context.Asset = asset;
        Log.Persisted(logger, context.Item.RelativePath, asset.Id);
        await next(context, ct);
    }

    private MediaAsset BuildAsset(IngestionContext context)
    {
        var (relativePath, mediaType) = context.Item switch
        {
            SingleFileItem s => (s.RelativePath, s.MediaType),
            LivePhotoPairItem l => (l.RelativePath, MediaType.LivePhoto),
            _ => throw new PipelineConfigurationException($"Unsupported item type: {context.Item.GetType().Name}"),
        };

        var exif = context.Exif!;
        // LocalDateTime.FromDateTime ignores DateTimeKind, so LastWriteTimeUtc component values
        // are copied directly — 2023-06-15 12:00:00 UTC → LocalDateTime(2023, 6, 15, 12, 0, 0).
        var dateCaptured = exif.DateCaptured ?? FallbackDate(context.Item.AbsolutePath);

        return new MediaAsset
        {
            Id = Guid.NewGuid(),
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

    private MediaAsset BuildMovAsset(LivePhotoPairItem item, IngestionContext context)
    {
        var exif = context.Exif!;
        var dateCaptured = exif.DateCaptured ?? FallbackDate(item.MovAbsolutePath);

        return new MediaAsset
        {
            Id = Guid.NewGuid(),
            StorageConfigId = context.Config.Id,
            FilePath = item.MovRelativePath,
            FileName = Path.GetFileName(item.MovRelativePath),
            ContentHash = context.MovContentHash!,
            DateCaptured = dateCaptured,
            Month = dateCaptured.Month,
            Day = dateCaptured.Day,
            Year = dateCaptured.Year,
            MediaType = MediaType.Video,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
        };
    }

    private LocalDateTime FallbackDate(string absolutePath)
    {
        var lastWriteUtc = fileSystem.FileInfo.New(absolutePath).LastWriteTimeUtc;
        return LocalDateTime.FromDateTime(lastWriteUtc);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Persisted {RelativePath} as asset {AssetId}")]
        public static partial void Persisted(ILogger logger, string relativePath, Guid assetId);
    }
}
