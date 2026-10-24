using Anichron.Core.Work;

namespace Anichron.Worker.Crawling;

/// <summary>A crawl's results summary. A Live Photo pair counts as one item.</summary>
/// <param name="Found">Items the walk queued for ingestion.</param>
/// <param name="Processed">Items ingested without an error.</param>
/// <param name="Failed">Items whose ingestion failed.</param>
public sealed record CrawlResult(long Found, long Processed, long Failed)
{
    /// <summary>The duplicate report for the crawled config, or null when the report failed.</summary>
    public DuplicateCounts? Duplicates { get; init; }
}

/// <summary>What one crawl's walk and ingestion counted.</summary>
/// <param name="Result">The crawl's results summary, before the duplicate report.</param>
/// <param name="DuplicateCopies">Copies within the crawled config, counted by this crawl only.</param>
internal sealed record CrawlRun(CrawlResult Result, int DuplicateCopies);

internal sealed class CrawlTally(IWorkProgress progress)
{
    private readonly Lock gate = new();
    private long found;
    private long processed;
    private long failed;
    private int duplicateCopies;
    private bool walked;

    public CrawlRun Run
    {
        get
        {
            lock (gate)
                return new CrawlRun(new CrawlResult(found, processed, failed), duplicateCopies);
        }
    }

    public void Found() => Update(() => found++);

    public void Walked() => Update(() => walked = true);

    public void Processed(int duplicateCopiesOfItem) => Update(() =>
    {
        processed++;
        duplicateCopies += duplicateCopiesOfItem;
    });

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
