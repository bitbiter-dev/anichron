using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed class IngestionClassifier(IMediaAssetRepository repository, IFileSystem fileSystem)
{
    public async Task<IngestionClassification> ClassifyAsync(IngestionContext context, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first and set both hashes; suppression is safe.
        var (item, contentHash, secondaryHash) = (context.Item, context.ContentHash!, context.SecondaryHash!);
        var mov = item.SecondaryFile;
        string[] hashes = mov is null ? [contentHash] : [contentHash, secondaryHash];
        string[] paths = mov is null ? [item.RelativePath] : [item.RelativePath, mov.RelativePath];

        var rows = await repository.FindByHashesOrPathsAsync(context.Config.Id, hashes, paths, ct);

        var rootPath = context.Config.RootPath;
        var primary = Classify(rows, contentHash, item.RelativePath, rootPath);
        var secondary = mov is null ? null : Classify(rows, secondaryHash, mov.RelativePath, rootPath);
        return new(primary, secondary, PairingFor(primary.Stored, secondary));
    }

    private FileClassification Classify(IReadOnlyList<MediaAsset> rows, string contentHash, string relativePath, string rootPath)
    {
        var stored = rows.FirstOrDefault(row => !row.IsSoftDeleted && row.ContentHash == contentHash);
        if (stored is null)
            return new(FileOutcome.New, null);
        if (stored.FilePath == relativePath)
            return new(FileOutcome.Unchanged, stored);
        if (fileSystem.File.Exists(fileSystem.Path.Combine(rootPath, stored.FilePath)))
            return new(FileOutcome.DuplicateCopy, stored);
        if (rows.Any(row => row.FilePath == relativePath))
            return new(FileOutcome.PathTaken, stored);
        return new(FileOutcome.Moved, stored);
    }

    private static PairingAction PairingFor(MediaAsset? storedPrimary, FileClassification? secondary)
        => (storedPrimary, secondary) switch
        {
            (null, _) or (_, null) => PairingAction.None,
            ({ PairedAssetId: null }, _) => PairingAction.AttachMov,
            ({ PairedAssetId: var pairedId }, { Stored.Id: var movId }) when pairedId == movId => PairingAction.BackfillMovProxy,
            _ => PairingAction.None,
        };
}
