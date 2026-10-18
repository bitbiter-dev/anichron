namespace Anichron.Worker.Ingestion;

internal static class IngestionShutdown
{
    // The one definition of "this is a clean shutdown, not an ingestion failure". The compensation
    // in the pipeline runner, the consumer loop's failure handling and the crawl loop's per-config
    // failure handling all branch on it, and they must never drift apart: a shutdown keeps the
    // proxies it already completed and stops taking new files and configs, where a failure cleans
    // up after itself and the crawl moves on.
    internal static bool IsInProgress(Exception exception, CancellationToken ct)
        => exception is OperationCanceledException && ct.IsCancellationRequested;
}
