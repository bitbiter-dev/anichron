using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Classification;

internal enum FileOutcome
{
    New = 0,
    Unchanged = 1,
    Moved = 2,
    DuplicateCopy = 3,
    PathTaken = 4,
    ContentChanged = 5,
    OverwrittenByCopy = 6,
    OverwrittenByMove = 7,
    AutoRestored = 8,
    AutoRestoredMoved = 9,
    AutoRestoredContentChanged = 10,
    LoneLivePhotoVideo = 11,
}

internal enum PairingAction
{
    None = 0,
    AttachMov = 1,
    BackfillMovProxy = 2,
}

/// <summary>What one crawled file is, and the stored rows its outcome acts on.</summary>
/// <param name="Outcome">What the crawled file is.</param>
/// <param name="Stored">The row the outcome acts on.</param>
/// <param name="Retired">The row the outcome soft-deletes.</param>
internal sealed record FileClassification(FileOutcome Outcome, MediaAsset? Stored, MediaAsset? Retired = null);

internal sealed record IngestionClassification(
    FileClassification Primary,
    FileClassification? Secondary,
    PairingAction Pairing)
{
    public bool NeedsProcessing
        => Primary.Outcome is FileOutcome.New or FileOutcome.ContentChanged or FileOutcome.AutoRestoredContentChanged;

    public int DuplicateCopies => NeedsProcessing
        ? 0
        : new[] { Primary, Secondary }.Count(file => file?.Outcome is FileOutcome.DuplicateCopy or FileOutcome.OverwrittenByCopy);
}
