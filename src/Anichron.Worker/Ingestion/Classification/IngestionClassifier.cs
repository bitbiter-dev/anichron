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

        var rows = await repository.FindByHashesOrPathsAsync(context.Config.Id, hashes, paths, ct);

        var rootPath = context.Config.RootPath;
        var primary = Classify(rows, contentHash, item.RelativePath, rootPath, isMov: false);
        var secondary = mov is null ? null : Classify(rows, secondaryHash, mov.RelativePath, rootPath, isMov: true);
        return new(primary, secondary, PairingFor(primary, secondary));
    }

    private FileClassification Classify(
        IReadOnlyList<MediaAsset> rows, string contentHash, string relativePath, string rootPath, bool isMov)
    {
        var atPath = rows.FirstOrDefault(row => row.FilePath == relativePath);
        var stored = rows.FirstOrDefault(row => !row.IsSoftDeleted && row.ContentHash == contentHash);
        if (stored is null)
            return ClassifyUnstoredContent(rows, contentHash, atPath);
        if (stored == atPath)
            return new(FileOutcome.Unchanged, stored);
        if (fileSystem.File.Exists(fileSystem.Path.Combine(rootPath, stored.FilePath)))
        {
            return atPath is { IsSoftDeleted: false } && !isMov
                ? new(FileOutcome.OverwrittenByCopy, stored, Retired: atPath)
                : new(FileOutcome.DuplicateCopy, stored);
        }

        if (atPath is null)
            return new(FileOutcome.Moved, stored);
        return isMov ? new(FileOutcome.PathTaken, stored) : new(FileOutcome.OverwrittenByMove, atPath, Retired: stored);
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

    private static PairingAction PairingFor(FileClassification primary, FileClassification? secondary)
        => (primary, secondary) switch
        {
            ({ Stored: null }
                or { Outcome: FileOutcome.ContentChanged or FileOutcome.AutoRestoredContentChanged or FileOutcome.OverwrittenByMove },
                _) or (_, null) => PairingAction.None,
            ({ Stored.PairedAssetId: null }, _) => PairingAction.AttachMov,
            ({ Stored.PairedAssetId: var pairedId }, { Stored.Id: var movId }) when pairedId == movId => PairingAction.BackfillMovProxy,
            _ => PairingAction.None,
        };
}
