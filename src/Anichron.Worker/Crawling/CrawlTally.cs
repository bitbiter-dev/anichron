using Anichron.Core.Work;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Crawling;

/// <summary>A crawl's results summary. A Live Photo pair counts as one item.</summary>
/// <param name="Found">Items the walk queued for ingestion.</param>
/// <param name="Processed">Items handled without an error, unchanged ones included.</param>
/// <param name="Failed">Items whose ingestion failed.</param>
public sealed record CrawlResult(long Found, long Processed, long Failed)
{
    /// <summary>Processed items whose content was read: new, edited, or restored with changed content.</summary>
    public long Ingested { get; init; }

    /// <summary>Files the walk reached and found no media type for; excluded folders and files are not counted.</summary>
    public int SkippedUnsupported { get; init; }

    /// <summary>The duplicate report for the crawled config, or null when the report failed.</summary>
    public DuplicateCounts? Duplicates { get; init; }

    public ReconciliationCounts? Reconciliation { get; init; }

    /// <summary>Links the walk skipped, to files or folders: a crawl never follows a link.</summary>
    public int SkippedLinks { get; init; }
}

/// <summary>How a crawl brought the config's rows in line with its files.</summary>
/// <param name="SoftDeleted">Rows soft-deleted, while ingesting or because the walk did not find their file.</param>
/// <param name="AutoRestored">Soft-deleted rows made active again because the crawl found their file.</param>
/// <param name="SkippedBecause">Why missing files were not looked for, or null when they were.</param>
public sealed record ReconciliationCounts(int SoftDeleted, int AutoRestored, MissingFileSkip? SkippedBecause);

/// <summary>What one crawl's walk and ingestion counted.</summary>
/// <param name="Result">The crawl's results summary, before the duplicate report.</param>
/// <param name="DuplicateCopies">Copies within the crawled config, counted by this crawl only.</param>
internal sealed record CrawlRun(CrawlResult Result, int DuplicateCopies)
{
    /// <summary>Rows soft-deleted while ingesting, because their path now holds other stored content.</summary>
    public int SoftDeleted { get; init; }

    public int AutoRestored { get; init; }

    /// <summary>Relative paths of every media file the walk queued, both halves of a Live Photo included.</summary>
    public IReadOnlySet<string> FoundPaths { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Whether the walk read every folder under the root.</summary>
    public bool WalkComplete { get; init; }
}

internal sealed class CrawlTally(IWorkProgress progress)
{
    private readonly Lock gate = new();
    private readonly HashSet<string> foundPaths = new(StringComparer.Ordinal);
    private long found;
    private long processed;
    private long failed;
    private long ingested;
    private int duplicateCopies;
    private int softDeleted;
    private int autoRestored;
    private int skippedLinks;
    private int skippedUnsupported;
    private bool walked;
    private bool folderSkipped;

    public CrawlRun Run
    {
        get
        {
            lock (gate)
            {
                var result = new CrawlResult(found, processed, failed)
                {
                    Ingested = ingested,
                    SkippedLinks = skippedLinks,
                    SkippedUnsupported = skippedUnsupported,
                };
                return new CrawlRun(result, duplicateCopies)
                {
                    SoftDeleted = softDeleted,
                    AutoRestored = autoRestored,
                    FoundPaths = new HashSet<string>(foundPaths, StringComparer.Ordinal),
                    WalkComplete = walked && !folderSkipped,
                };
            }
        }
    }

    public void Found(IngestionItem item) => Update(() =>
    {
        found++;
        foundPaths.Add(item.RelativePath);
        if (item.SecondaryFile is { } secondary)
            foundPaths.Add(secondary.RelativePath);
    });

    public void FolderSkipped()
    {
        lock (gate)
            folderSkipped = true;
    }

    public void LinkSkipped()
    {
        lock (gate)
            skippedLinks++;
    }

    public void UnsupportedSkipped()
    {
        lock (gate)
            skippedUnsupported++;
    }

    public void Walked() => Update(() => walked = true);

    public void Processed(IngestionContext context) => Update(() =>
    {
        processed++;
        if (context.Classification?.NeedsProcessing == true)
            ingested++;
        duplicateCopies += context.Classification?.DuplicateCopies ?? 0;
        softDeleted += context.SoftDeleted;
        autoRestored += context.AutoRestored;
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
