using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Classification;

internal enum FileOutcome
{
    New = 0,
    Unchanged = 1,
    Moved = 2,
    DuplicateCopy = 3,
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
    PointAtMov = 3,
}

/// <summary>What one crawled file is, and the stored rows its outcome acts on.</summary>
/// <param name="Outcome">What the crawled file is.</param>
/// <param name="Stored">The row the outcome acts on.</param>
/// <param name="Retired">The row the outcome soft-deletes.</param>
internal sealed record FileClassification(FileOutcome Outcome, MediaAsset? Stored, MediaAsset? Retired = null)
{
    /// <summary>The row that describes the file at its path once the outcome is applied; null when the file is new or a copy.</summary>
    public MediaAsset? Row => DefersToAnotherRow ? null : Stored;

    /// <summary>Whether the file holds content another row keeps, so it has no row of its own.</summary>
    public bool DefersToAnotherRow => Outcome is FileOutcome.DuplicateCopy or FileOutcome.OverwrittenByCopy or FileOutcome.LoneLivePhotoVideo;

    /// <summary>Whether <see cref="Stored"/> takes content it does not hold yet.</summary>
    public bool TakesContent
        => Outcome is FileOutcome.ContentChanged or FileOutcome.AutoRestoredContentChanged or FileOutcome.OverwrittenByMove;
}

internal sealed record IngestionClassification(
    FileClassification Primary,
    FileClassification? Secondary,
    PairingAction Pairing)
{
    public bool NeedsProcessing
        => Primary.Outcome is FileOutcome.New or FileOutcome.ContentChanged or FileOutcome.AutoRestoredContentChanged;

    /// <summary>Whether the video takes content and is dated from the processed photo, once the photo is processed.</summary>
    public bool DatesMovAfterProcessing => NeedsProcessing && Secondary is { TakesContent: true };

    /// <summary>
    /// The rows soft-deleted before the item's save, so another row can take their content: every half's retired
    /// row once either half was overwritten by a move, otherwise none.
    /// </summary>
    public IReadOnlyList<MediaAsset> RetiredFirst
        => Halves.Any(file => file.Outcome == FileOutcome.OverwrittenByMove)
            ? [.. Halves.Select(file => file.Retired).OfType<MediaAsset>()]
            : [];

    private IEnumerable<FileClassification> Halves => Secondary is null ? [Primary] : [Primary, Secondary];

    public int DuplicateCopies => NeedsProcessing
        ? 0
        : new[] { Primary, Secondary }.Count(file => file?.Outcome is FileOutcome.DuplicateCopy or FileOutcome.OverwrittenByCopy);
}
