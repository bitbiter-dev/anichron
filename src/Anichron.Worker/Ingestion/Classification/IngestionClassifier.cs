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
        var primary = Classify(rows, contentHash, item.RelativePath, rootPath);
        var secondary = mov is null ? null : Classify(rows, secondaryHash, mov.RelativePath, rootPath);
        return new(primary, secondary, PairingFor(primary, secondary));
    }

    private FileClassification Classify(IReadOnlyList<MediaAsset> rows, string contentHash, string relativePath, string rootPath)
    {
        var stored = rows.FirstOrDefault(row => !row.IsSoftDeleted && row.ContentHash == contentHash);
        if (stored is null)
            return ClassifyUnknownContent(rows, relativePath);
        if (stored.FilePath == relativePath)
            return new(FileOutcome.Unchanged, stored);
        if (fileSystem.File.Exists(fileSystem.Path.Combine(rootPath, stored.FilePath)))
            return new(FileOutcome.DuplicateCopy, stored);
        if (rows.Any(row => row.FilePath == relativePath))
            return new(FileOutcome.PathTaken, stored);
        return new(FileOutcome.Moved, stored);
    }

    private static FileClassification ClassifyUnknownContent(IReadOnlyList<MediaAsset> rows, string relativePath)
    {
        var atPath = rows.FirstOrDefault(row => !row.IsSoftDeleted && row.FilePath == relativePath);
        return atPath is null ? new(FileOutcome.New, null) : new(FileOutcome.ContentChanged, atPath);
    }

    private static PairingAction PairingFor(FileClassification primary, FileClassification? secondary)
        => (primary, secondary) switch
        {
            ({ Stored: null } or { Outcome: FileOutcome.ContentChanged }, _) or (_, null) => PairingAction.None,
            ({ Stored.PairedAssetId: null }, _) => PairingAction.AttachMov,
            ({ Stored.PairedAssetId: var pairedId }, { Stored.Id: var movId }) when pairedId == movId => PairingAction.BackfillMovProxy,
            _ => PairingAction.None,
        };
}
