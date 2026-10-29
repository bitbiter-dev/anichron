using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed class IngestionClassifier(IMediaAssetRepository repository, IFileSystem fileSystem)
{
    public async Task<IngestionClassification> ClassifyAsync(IngestionContext context, CancellationToken ct)
    {
        var (item, contentHash, secondaryHash) = (context.Item, context.ContentHash!, context.SecondaryHash!);
        var mov = item.SecondaryFile;
        string[] hashes = mov is null ? [contentHash] : [contentHash, secondaryHash];
        string[] paths = mov is null ? [item.RelativePath] : [item.RelativePath, mov.RelativePath];

        var matched = await repository.FindByHashesOrPathsAsync(context.Config.Id, hashes, paths, ct);
        var rows = matched.Select(row => row.Asset).ToList();

        var rootPath = context.Config.RootPath;
        var primary = (mov is null ? LoneLivePhotoVideo(matched, item.RelativePath, ownVideoIds: []) : null)
            ?? Classify(rows, contentHash, item.RelativePath, rootPath, AtPath(rows, item.RelativePath));
        var secondary = mov is null ? null : ClassifyVideo(matched, rows, primary, secondaryHash, mov.RelativePath, rootPath);
        return new(primary, secondary, PairingFor(primary, secondary));
    }

    private FileClassification ClassifyVideo(
        IReadOnlyList<MatchedAsset> matched,
        IReadOnlyList<MediaAsset> rows,
        FileClassification photo,
        string contentHash,
        string relativePath,
        string rootPath)
    {
        var paired = rows.FirstOrDefault(row => row.Id == photo.Row?.PairedAssetId);
        if (paired is not null)
        {
            var holder = rows.FirstOrDefault(row => row.FilePath == relativePath && row != paired);
            return Classify(rows, contentHash, relativePath, rootPath, holder ?? paired);
        }

        var movedPhoto = photo.Outcome == FileOutcome.OverwrittenByMove ? photo.Retired : null;
        return LoneLivePhotoVideo(matched, relativePath, [photo.Row?.PairedAssetId, movedPhoto?.PairedAssetId])
            ?? Classify(rows, contentHash, relativePath, rootPath, AtPath(rows, relativePath));
    }

    private static MediaAsset? AtPath(IReadOnlyList<MediaAsset> rows, string relativePath)
        => rows.FirstOrDefault(row => row.FilePath == relativePath);

    private static FileClassification? LoneLivePhotoVideo(
        IReadOnlyList<MatchedAsset> matched, string relativePath, Guid?[] ownVideoIds)
    {
        if (matched.FirstOrDefault(row => row.IsLivePhotoVideo && !ownVideoIds.Contains(row.Asset.Id)) is not { } video)
            return null;

        var atPath = matched.FirstOrDefault(row => row.Asset.FilePath == relativePath);
        return new(
            FileOutcome.LoneLivePhotoVideo,
            video.Asset,
            Retired: atPath is { IsLivePhotoVideo: false, Asset.IsSoftDeleted: false } ? atPath.Asset : null);
    }

    private FileClassification Classify(
        IReadOnlyList<MediaAsset> rows, string contentHash, string relativePath, string rootPath, MediaAsset? atPath)
    {
        var stored = rows.FirstOrDefault(row => !row.IsSoftDeleted && row.ContentHash == contentHash);
        if (stored is null)
            return ClassifyUnstoredContent(rows, contentHash, atPath);
        if (stored == atPath)
            return new(stored.FilePath == relativePath ? FileOutcome.Unchanged : FileOutcome.Moved, stored);
        if (fileSystem.File.Exists(fileSystem.Path.Combine(rootPath, stored.FilePath)))
        {
            return atPath is { IsSoftDeleted: false }
                ? new(FileOutcome.OverwrittenByCopy, stored, Retired: atPath)
                : new(FileOutcome.DuplicateCopy, stored);
        }

        return atPath is null ? new(FileOutcome.Moved, stored) : new(FileOutcome.OverwrittenByMove, atPath, Retired: stored);
    }

    private static FileClassification ClassifyUnstoredContent(
        IReadOnlyList<MediaAsset> rows, string contentHash, MediaAsset? atPath)
    {
        if (atPath is { IsSoftDeleted: false })
            return new(FileOutcome.ContentChanged, atPath);
        if (atPath is not null)
        {
            return atPath.ContentHash == contentHash
                ? new(FileOutcome.AutoRestored, atPath)
                : new(FileOutcome.AutoRestoredContentChanged, atPath);
        }

        var softDeleted = rows
            .Where(row => row.ContentHash == contentHash)
            .MaxBy(row => row.LastSeenOnNas);
        return softDeleted is null ? new(FileOutcome.New, null) : new(FileOutcome.AutoRestoredMoved, softDeleted);
    }

    private static PairingAction PairingFor(FileClassification photo, FileClassification? video)
    {
        if (photo.Row is not { } photoRow
            || photo.Outcome is FileOutcome.ContentChanged or FileOutcome.AutoRestoredContentChanged
            || video is not { DefersToAnotherRow: false })
        {
            return PairingAction.None;
        }

        if (video.Row is not { } videoRow || photoRow.PairedAssetId != videoRow.Id)
            return video.TakesContent ? PairingAction.PointAtMov : PairingAction.AttachMov;
        return video.TakesContent ? PairingAction.None : PairingAction.BackfillMovProxy;
    }
}
