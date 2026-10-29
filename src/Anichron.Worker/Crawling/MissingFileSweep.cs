using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using System.Text.Json.Serialization;

namespace Anichron.Worker.Crawling;

/// <summary>Why a crawl soft-deleted nothing, though rows may be missing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissingFileSkip>))]
public enum MissingFileSkip
{
    /// <summary>The walk skipped a folder it could not read.</summary>
    IncompleteWalk = 0,

    /// <summary>The walk found no media files while the config has active rows, as when a share is not mounted.</summary>
    EmptyRoot = 1,
}

/// <summary>What a crawl's missing-file sweep did.</summary>
/// <param name="SoftDeleted">Rows the sweep soft-deleted.</param>
/// <param name="SkippedBecause">Why the sweep soft-deleted nothing, or null when it ran.</param>
internal sealed record MissingFiles(int SoftDeleted, MissingFileSkip? SkippedBecause);

internal interface IMissingFileSweep
{
    Task<MissingFiles> SweepAsync(UserStorageConfig config, CrawlRun run, CancellationToken ct);
}

internal sealed partial class MissingFileSweep(
    IMediaAssetRepository repository,
    ILogger<MissingFileSweep> logger) : IMissingFileSweep
{
    public async Task<MissingFiles> SweepAsync(UserStorageConfig config, CrawlRun run, CancellationToken ct)
    {
        if (!run.WalkComplete)
            return new MissingFiles(0, MissingFileSkip.IncompleteWalk);

        var active = await repository.GetActivePathsAsync(config.Id, ct);
        if (run.FoundPaths.Count == 0 && active.Count > 0)
            return new MissingFiles(0, MissingFileSkip.EmptyRoot);

        var missing = active.Where(row => !run.FoundPaths.Contains(row.FilePath)).ToList();
        var missingIds = missing.Select(row => row.Id).ToHashSet();
        var videosOfMissingPhotos = missing.Select(row => row.PairedAssetId).OfType<Guid>().ToHashSet();
        var videosLeftBehind = active
            .Where(row => !missingIds.Contains(row.Id) && videosOfMissingPhotos.Contains(row.Id))
            .ToList();
        if (missing.Count + videosLeftBehind.Count == 0)
            return new MissingFiles(0, null);

        foreach (var row in missing)
            Log.SoftDeleting(logger, row.Id, row.FilePath);
        foreach (var row in videosLeftBehind)
            Log.SoftDeletingLeftBehindVideo(logger, row.Id, row.FilePath);
        foreach (var (photo, video) in
            from photo in active
            where !missingIds.Contains(photo.Id)
            join video in missing on photo.PairedAssetId equals video.Id
            select (photo, video))
        {
            Log.NowAStillPhoto(logger, photo.Id, video.Id, video.FilePath);
        }

        return new MissingFiles(
            await repository.SoftDeleteAsync([.. missing.Concat(videosLeftBehind).Select(row => row.Id)], ct), null);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Soft-deleting asset {AssetId} at {FilePath}: a complete crawl did not find it.")]
        public static partial void SoftDeleting(ILogger logger, Guid assetId, string filePath);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Soft-deleting asset {AssetId} at {FilePath}: it is the video of a Live Photo whose photo is missing.")]
        public static partial void SoftDeletingLeftBehindVideo(ILogger logger, Guid assetId, string filePath);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Asset {PhotoId} is now a still photo: its video, asset {VideoId} at {FilePath}, is missing.")]
        public static partial void NowAStillPhoto(ILogger logger, Guid photoId, Guid videoId, string filePath);
    }
}
