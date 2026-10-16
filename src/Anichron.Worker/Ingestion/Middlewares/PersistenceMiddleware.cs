using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class PersistenceMiddleware(
    IMediaAssetRepository repository,
    IUnitOfWork unitOfWork,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<PersistenceMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.Persistence;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var asset = BuildAsset(context);
        MediaAsset? mov = null;

        if (context.Item.SecondaryFile is { } secondary)
        {
            // Ordering guarantees ContentHashingMiddleware ran first and set SecondaryHash; suppression is safe.
            mov = await pairedMovResolver.ResolveAsync(
                secondary, context.SecondaryHash!, context.Config.Id, context.Exif!.DateCaptured, ct);
            await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
            asset.PairedAssetId = mov.Id;
        }

        repository.Add(asset);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            if (await repository.FindByHashAsync(asset.ContentHash, asset.StorageConfigId, ct) is null)
            {
                if (mov is not null)
                    context.ReleaseProxyFiles(mov.Id);
                throw;
            }

            Log.AlreadyPersisted(logger, context.Item.RelativePath);
            return;
        }

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
            ProxyFiles = [.. context.ProxyFiles.Where(proxy => proxy.AssetId == context.AssetId)],
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
            },
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

        [LoggerMessage(Level = LogLevel.Debug, Message = "Content of {RelativePath} was persisted concurrently by another consumer, skipping.")]
        public static partial void AlreadyPersisted(ILogger logger, string relativePath);
    }
}
