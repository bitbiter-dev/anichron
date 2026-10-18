using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class KnownFileReconciler(IClock clock, ILogger<KnownFileReconciler> logger)
{
    public bool Reconcile(FileClassification file, string relativePath) => file.Outcome switch
    {
        FileOutcome.Unchanged => Refresh(file.Stored!, relativePath),
        FileOutcome.Moved => Move(file.Stored!, relativePath),
        FileOutcome.DuplicateCopy => SkipDuplicate(file.Stored!, relativePath),
        FileOutcome.PathTaken => SkipTakenPath(relativePath),
        _ => false,
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
        stored.FilePath = relativePath;
        stored.FileName = Path.GetFileName(relativePath);
        stored.LastSeenOnNas = clock.GetCurrentInstant();
        return true;
    }

    private bool SkipDuplicate(MediaAsset stored, string relativePath)
    {
        Log.DuplicateCopy(logger, relativePath, stored.FilePath);
        return false;
    }

    private bool SkipTakenPath(string relativePath)
    {
        Log.PathTaken(logger, relativePath);
        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Already ingested {RelativePath}, refreshed last seen.")]
        public static partial void AlreadyIngested(ILogger logger, string relativePath);

        [LoggerMessage(Level = LogLevel.Debug, Message = "{RelativePath} is a copy of already-ingested {StoredPath}, skipping.")]
        public static partial void DuplicateCopy(ILogger logger, string relativePath, string storedPath);

        [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} moved from {OldPath} to {NewPath}")]
        public static partial void Moved(ILogger logger, Guid assetId, string oldPath, string newPath);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot record {RelativePath}: another asset row already holds that path, skipping.")]
        public static partial void PathTaken(ILogger logger, string relativePath);
    }
}
