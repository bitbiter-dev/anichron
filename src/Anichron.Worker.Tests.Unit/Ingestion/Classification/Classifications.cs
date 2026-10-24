using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

internal static class Classifications
{
    public static IngestionClassification Of(FileOutcome primary, FileOutcome? secondary = null)
        => new(
            new FileClassification(primary, null),
            secondary is { } outcome ? new FileClassification(outcome, null) : null,
            PairingAction.None);
}
