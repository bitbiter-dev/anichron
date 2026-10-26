using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class KnownFileReconciler(
    IClock clock,
    IMediaAssetRepository repository,
    IBurstRepository bursts,
    IGuidFactory guidFactory,
    ILogger<KnownFileReconciler> logger)
{
    public Task<bool> ReconcileAsync(
        IngestionContext context, FileClassification file, string relativePath, CancellationToken ct) => file.Outcome switch
        {
            FileOutcome.Unchanged => Task.FromResult(Refresh(file.Stored!, relativePath)),
            FileOutcome.Moved => Task.FromResult(Move(file.Stored!, relativePath)),
            FileOutcome.DuplicateCopy => Task.FromResult(SkipDuplicate(file.Stored!, relativePath)),
            FileOutcome.PathTaken => Task.FromResult(SkipTakenPath(relativePath)),
            FileOutcome.OverwrittenByCopy => Task.FromResult(RetireOverwritten(context, file.Stored!, file.Retired!, relativePath)),
            FileOutcome.OverwrittenByMove => TakeMovedContentAsync(context, file.Stored!, file.Retired!, relativePath, ct),
            FileOutcome.AutoRestored or FileOutcome.AutoRestoredContentChanged
                => AutoRestoreAsync(context, file.Stored!, file.Stored!.FilePath, ct),
            FileOutcome.AutoRestoredMoved => AutoRestoreAsync(context, file.Stored!, relativePath, ct),
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

    private bool SkipTakenPath(string relativePath)
    {
        Log.PathTaken(logger, relativePath);
        return false;
    }

    private bool RetireOverwritten(IngestionContext context, MediaAsset copied, MediaAsset atPath, string relativePath)
    {
        SkipDuplicate(copied, relativePath);
        atPath.IsSoftDeleted = true;
        context.CountSoftDeleted();
        Log.OverwrittenByCopy(logger, atPath.Id, relativePath, copied.FilePath);
        return true;
    }

    private async Task<bool> TakeMovedContentAsync(
        IngestionContext context, MediaAsset atPath, MediaAsset moved, string relativePath, CancellationToken ct)
    {
        await repository.LoadMetadataAsync(atPath, ct);
        await repository.LoadMetadataAsync(moved, ct);

        atPath.ContentHash = moved.ContentHash;
        atPath.DateCaptured = moved.DateCaptured;
        atPath.MediaType = moved.MediaType;
        CopyMetadata(moved.Metadata ?? new Metadata(), atPath.Metadata ??= new Metadata { AssetId = atPath.Id });
        ProxyFileRows.Replace(repository, atPath, [.. moved.ProxyFiles.Select(proxy => ProxyOf(atPath, proxy))]);
        atPath.BurstId = null;
        await ReactivateAsync(context, atPath, ct);

        moved.IsSoftDeleted = true;
        context.CountSoftDeleted();
        Log.OverwrittenByMove(logger, atPath.Id, relativePath, moved.Id, moved.FilePath);
        return true;
    }

    private async Task<bool> AutoRestoreAsync(IngestionContext context, MediaAsset trashed, string relativePath, CancellationToken ct)
    {
        MoveTo(trashed, relativePath);
        await ReactivateAsync(context, trashed, ct);
        Log.AutoRestored(logger, trashed.Id, relativePath);
        return true;
    }

    private Task ReactivateAsync(IngestionContext context, MediaAsset asset, CancellationToken ct)
    {
        if (asset.IsSoftDeleted)
            context.CountAutoRestored();
        asset.IsSoftDeleted = false;
        asset.LastSeenOnNas = clock.GetCurrentInstant();
        return bursts.RemoveCheckAsync(asset.Id, ct);
    }

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

    private static void CopyMetadata(Metadata source, Metadata target)
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.Orientation = source.Orientation;
        target.Latitude = source.Latitude;
        target.Longitude = source.Longitude;
        target.CameraMake = source.CameraMake;
        target.CameraModel = source.CameraModel;
        target.LensModel = source.LensModel;
        target.DurationInSeconds = source.DurationInSeconds;
        target.Blurhash = source.Blurhash;
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

        [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot record {RelativePath}: another asset row already holds that path, skipping.")]
        public static partial void PathTaken(ILogger logger, string relativePath);

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
    }
}
