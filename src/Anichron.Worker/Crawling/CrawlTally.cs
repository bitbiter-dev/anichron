using Anichron.Core.Work;

namespace Anichron.Worker.Crawling;

/// <summary>A crawl's results summary. A Live Photo pair counts as one item.</summary>
/// <param name="Found">Items the walk queued for ingestion.</param>
/// <param name="Processed">Items ingested without an error.</param>
/// <param name="Failed">Items whose ingestion failed.</param>
public sealed record CrawlResult(long Found, long Processed, long Failed);

internal sealed class CrawlTally(IWorkProgress progress)
{
    private readonly Lock gate = new();
    private long found;
    private long processed;
    private long failed;
    private bool walked;

    public CrawlResult Result
    {
        get
        {
            lock (gate)
                return new CrawlResult(found, processed, failed);
        }
    }

    public void Found() => Update(() => found++);

    public void Walked() => Update(() => walked = true);

    public void Processed() => Update(() => processed++);

    public void Failed() => Update(() => failed++);

    private void Update(Action change)
    {
        lock (gate)
        {
            change();
            progress.Report(
                processed + failed,
                walked ? found : null,
                $"{found} found, {processed} processed, {failed} failed");
        }
    }
}
