namespace Anichron.Worker.Ingestion;

internal static class IngestionCancellation
{
    // A shutdown or an admin's cancel, never an ingestion failure.
    internal static bool IsRequested(Exception exception, CancellationToken ct)
        => exception is OperationCanceledException && ct.IsCancellationRequested;
}
