using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Classification;

internal enum FileOutcome
{
    New = 0,
    Unchanged = 1,
    Moved = 2,
    DuplicateCopy = 3,
    PathTaken = 4,
}

internal enum PairingAction
{
    None = 0,
    AttachMov = 1,
    BackfillMovProxy = 2,
}

internal sealed record FileClassification(FileOutcome Outcome, MediaAsset? Stored);

internal sealed record IngestionClassification(
    FileClassification Primary,
    FileClassification? Secondary,
    PairingAction Pairing)
{
    public bool NeedsProcessing => Primary.Outcome == FileOutcome.New;
}
