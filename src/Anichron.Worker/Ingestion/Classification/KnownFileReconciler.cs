using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class KnownFileReconciler(
    IClock clock,
    IMediaAssetRepository repository,
    StoredContent storedContent,
    IGuidFactory guidFactory,
    PairedMovProxyWriter pairedMovProxyWriter,
    IFileSystem fileSystem,
    ILogger<KnownFileReconciler> logger)
{
    public Task<bool> ReconcileAsync(
        IngestionContext context, FileClassification file, string relativePath, CancellationToken ct) => file.Outcome switch
        {
            FileOutcome.Unchanged => Task.FromResult(Refresh(file.Stored!, relativePath)),
            FileOutcome.Moved => Task.FromResult(Move(file.Stored!, relativePath)),
            FileOutcome.DuplicateCopy => Task.FromResult(SkipDuplicate(file.Stored!, relativePath)),
            FileOutcome.OverwrittenByCopy => RetireOverwrittenAsync(context, file.Stored!, file.Retired!, relativePath, ct),
            FileOutcome.OverwrittenByMove => TakeMovedContentAsync(context, file.Stored!, file.Retired!, relativePath, ct),
            FileOutcome.ContentChanged or FileOutcome.AutoRestoredContentChanged
                => TakeVideoContentAsync(context, file.Stored!, relativePath, ct),
            FileOutcome.AutoRestored or FileOutcome.AutoRestoredMoved => AutoRestoreAsync(context, file.Stored!, relativePath, ct),
            FileOutcome.LoneLivePhotoVideo => LeaveLivePhotoVideoAsync(context, file.Stored!, file.Retired, relativePath, ct),
            _ => Task.FromResult(false),
        };

    private bool Refresh(MediaAsset stored, string relativePath)
    {
        stored.LastSeenOnNas = clock.GetCurrentInstant();
        Log.AlreadyIngested(logger, relativePath);
        return true;
    }

    private bool Move(MediaAsset stored, string relativePath)
    {
        Log.Moved(logger, stored.Id, stored.FilePath, relativePath);
        MoveTo(stored, relativePath);
        stored.LastSeenOnNas = clock.GetCurrentInstant();
        return true;
    }

    private bool SkipDuplicate(MediaAsset stored, string relativePath)
    {
        Log.DuplicateCopy(logger, stored.ContentHash, stored.StorageConfigId, relativePath, stored.FilePath);
        return false;
    }

    private async Task<bool> RetireOverwrittenAsync(
        IngestionContext context, MediaAsset copied, MediaAsset atPath, string relativePath, CancellationToken ct)
    {
        SkipDuplicate(copied, relativePath);
        await storedContent.RetireAsync(context, atPath, ct);
        Log.OverwrittenByCopy(logger, atPath.Id, relativePath, copied.FilePath);
        return true;
    }

    private async Task<bool> LeaveLivePhotoVideoAsync(
        IngestionContext context, MediaAsset video, MediaAsset? atPath, string relativePath, CancellationToken ct)
    {
        if (atPath is null)
        {
            Log.LeftLivePhotoVideo(logger, relativePath, video.Id);
            return false;
        }

        await storedContent.RetireAsync(context, atPath, ct);
        Log.OverwrittenByLivePhotoVideo(logger, atPath.Id, relativePath, video.Id, video.FilePath);
        return true;
    }

    private async Task<bool> TakeMovedContentAsync(
        IngestionContext context, MediaAsset atPath, MediaAsset moved, string relativePath, CancellationToken ct)
    {
        await repository.LoadMetadataAsync(moved, ct);
        MoveTo(atPath, relativePath);
        await storedContent.TakeAsync(
            context, atPath, UndatedWhenProcessingDates(context, AssetContent.Of(moved, [.. moved.ProxyFiles.Select(proxy => ProxyOf(atPath, proxy))])), ct);

        await storedContent.RetireAsync(context, moved, ct);
        Log.OverwrittenByMove(logger, atPath.Id, relativePath, moved.Id, moved.FilePath);
        return true;
    }

    private async Task<bool> TakeVideoContentAsync(
        IngestionContext context, MediaAsset video, string relativePath, CancellationToken ct)
    {
        Log.VideoContentChanged(logger, video.Id, relativePath, video.ContentHash, context.SecondaryHash!);
        MoveTo(video, relativePath);
        var proxies = await pairedMovProxyWriter.WriteForNewContentAsync(context, video, ct);
        var captured = context.Classification!.Primary.Row?.DateCaptured
            ?? CaptureDate.FromExifOrLastWrite(null, fileSystem, context.Item.SecondaryFile!.AbsolutePath);
        await storedContent.TakeAsync(
            context, video, UndatedWhenProcessingDates(context, new AssetContent(context.SecondaryHash!, captured, MediaType.Video, new Metadata(), proxies)), ct);
        return true;
    }

    private async Task<bool> AutoRestoreAsync(IngestionContext context, MediaAsset trashed, string relativePath, CancellationToken ct)
    {
        MoveTo(trashed, relativePath);
        await storedContent.ReactivateAsync(context, trashed, ct);
        Log.AutoRestored(logger, trashed.Id, relativePath);
        if (context.Classification?.Primary is { Stored: { } photo } primary
            && photo.PairedAssetId == trashed.Id
            && primary.Outcome is not (FileOutcome.AutoRestored or FileOutcome.AutoRestoredMoved or FileOutcome.AutoRestoredContentChanged))
        {
            Log.LivePhotoAgain(logger, photo.Id, trashed.Id, relativePath);
        }

        return true;
    }

    private static AssetContent UndatedWhenProcessingDates(IngestionContext context, AssetContent content)
        => context.Classification?.DatesMovAfterProcessing == true ? content.Undated() : content;

    private ProxyFile ProxyOf(MediaAsset owner, ProxyFile source) => new()
    {
        Id = guidFactory.NewGuid(),
        AssetId = owner.Id,
        ProxyType = source.ProxyType,
        ProxyPath = source.ProxyPath,
        SizeBytes = source.SizeBytes,
        CreatedAt = source.CreatedAt,
    };

    private static void MoveTo(MediaAsset asset, string relativePath)
    {
        asset.FilePath = relativePath;
        asset.FileName = Path.GetFileName(relativePath);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Already ingested {RelativePath}, refreshed last seen.")]
        public static partial void AlreadyIngested(ILogger logger, string relativePath);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Duplicate content {ContentHash} in storage config {StorageConfigId}: {RelativePath} is a copy of {StoredPath}.")]
        public static partial void DuplicateCopy(
            ILogger logger, string contentHash, Guid storageConfigId, string relativePath, string storedPath);

        [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} moved from {OldPath} to {NewPath}")]
        public static partial void Moved(ILogger logger, Guid assetId, string oldPath, string newPath);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Video asset {AssetId} at {RelativePath} changed content from {OldHash} to {NewHash}, updating it in place.")]
        public static partial void VideoContentChanged(ILogger logger, Guid assetId, string relativePath, string oldHash, string newHash);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Soft-deleted asset {AssetId} at {RelativePath}: the path now holds a copy of {CopiedPath}.")]
        public static partial void OverwrittenByCopy(ILogger logger, Guid assetId, string relativePath, string copiedPath);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Asset {AssetId} at {RelativePath} took the content of asset {MovedId}, moved there from {MovedPath}; "
                + "soft-deleted asset {MovedId}.")]
        public static partial void OverwrittenByMove(ILogger logger, Guid assetId, string relativePath, Guid movedId, string movedPath);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-restored asset {AssetId} at {RelativePath}.")]
        public static partial void AutoRestored(ILogger logger, Guid assetId, string relativePath);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Asset {PhotoId} is a Live Photo again: its video, asset {VideoId} at {RelativePath}, was auto-restored.")]
        public static partial void LivePhotoAgain(ILogger logger, Guid photoId, Guid videoId, string relativePath);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Left {RelativePath} alone: it holds the video of a Live Photo, asset {VideoId}, which follows only its photo.")]
        public static partial void LeftLivePhotoVideo(ILogger logger, string relativePath, Guid videoId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Soft-deleted asset {AssetId} at {RelativePath}: the path now holds a copy of the video of a Live Photo, "
                + "{VideoId} at {VideoPath}.")]
        public static partial void OverwrittenByLivePhotoVideo(
            ILogger logger, Guid assetId, string relativePath, Guid videoId, string videoPath);
    }
}
