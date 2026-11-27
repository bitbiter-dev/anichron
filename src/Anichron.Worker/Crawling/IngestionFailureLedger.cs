using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Crawling;

/// <summary>An item whose ingestion failed on every attempt of one crawl.</summary>
/// <param name="RelativePath">The item's primary path.</param>
/// <param name="Fingerprint">The item's files before the last attempt read them.</param>
/// <param name="Reason">The last attempt's exception type and message.</param>
internal sealed record FailedItem(string RelativePath, string Fingerprint, string Reason)
{
    public static FailedItem Of(IngestionItem item, string fingerprint, Exception failure)
        => new(item.RelativePath, fingerprint, $"{failure.GetType().Name}: {failure.Message}");
}

internal interface IIngestionFailureLedger
{
    Task<FailingFiles> LoadAsync(UserStorageConfig config, CancellationToken ct);

    /// <summary>Writes what a finished crawl learned about the config's failing files, in one save.</summary>
    /// <param name="config">The crawled config.</param>
    /// <param name="run">What the walk and ingestion found.</param>
    /// <param name="missing">Whether the missing-file sweep ran, which decides whether rows of files not reached go.</param>
    /// <param name="ct">Cancels the write.</param>
    Task RecordAsync(UserStorageConfig config, CrawlRun run, MissingFiles missing, CancellationToken ct);
}

internal sealed class IngestionFailureLedger(
    IIngestionFailureRepository repository,
    IUnitOfWork unitOfWork,
    IFileSystem fileSystem,
    IClock clock,
    IOptions<WorkerSettings> options) : IIngestionFailureLedger
{
    private readonly WorkerSettings settings = options.Value;

    public async Task<FailingFiles> LoadAsync(UserStorageConfig config, CancellationToken ct)
        => new(await LoadRowsAsync(config, ct), fileSystem, clock);

    public async Task RecordAsync(UserStorageConfig config, CrawlRun run, MissingFiles missing, CancellationToken ct)
    {
        var rows = await LoadRowsAsync(config, ct);
        var now = clock.GetCurrentInstant();
        foreach (var failure in run.Failures)
            Record(config.Id, rows.GetValueOrDefault(failure.RelativePath), failure, now);

        var prunes = missing.SkippedBecause is null && run.FoundPaths.Count > 0;
        foreach (var row in rows.Values.Where(row => run.SucceededPaths.Contains(row.FilePath)
            || (prunes && !run.ReachedPaths.Contains(row.FilePath))))
        {
            repository.Remove(row);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, IngestionFailure>> LoadRowsAsync(UserStorageConfig config, CancellationToken ct)
        => (await repository.GetByConfigAsync(config.Id, ct)).ToDictionary(row => row.FilePath, StringComparer.Ordinal);

    private void Record(Guid configId, IngestionFailure? row, FailedItem failure, Instant now)
    {
        var repeated = row?.Fingerprint == failure.Fingerprint;
        if (row is null)
        {
            row = new IngestionFailure
            {
                StorageConfigId = configId,
                FilePath = failure.RelativePath,
                Fingerprint = failure.Fingerprint,
                Reason = failure.Reason,
            };
            repository.Add(row);
        }

        row.Failures = repeated ? row.Failures + 1 : 1;
        if (!repeated)
            row.FirstFailedAt = now;
        row.Fingerprint = failure.Fingerprint;
        row.Reason = failure.Reason[..Math.Min(failure.Reason.Length, IngestionFailure.ReasonMaxLength)];
        row.LastFailedAt = now;
        row.RetryAfter = now + Backoff(row.Failures);
    }

    private Duration Backoff(int failures)
        => Duration.FromHours(Math.Min(
            settings.CrawlIntervalHours * (Math.Pow(2, failures - 1) - 0.5),
            settings.IngestionRetryMaxDays * 24));
}

/// <summary>A config's failing files as a crawl loaded them, and the fingerprint of an item's files.</summary>
/// <param name="rows">The config's rows by primary path.</param>
/// <param name="fileSystem">Reads the items' files.</param>
/// <param name="clock">Decides whether a row is due again.</param>
internal sealed class FailingFiles(
    IReadOnlyDictionary<string, IngestionFailure> rows,
    IFileSystem fileSystem,
    IClock clock)
{
    /// <summary>The item's row, when the item is not due again and its files have not changed since it failed.</summary>
    /// <param name="item">The item the walk reached.</param>
    public IngestionFailure? Skipping(IngestionItem item)
        => rows.TryGetValue(item.RelativePath, out var row)
            && clock.GetCurrentInstant() < row.RetryAfter
            && Fingerprint(item) == row.Fingerprint
                ? row
                : null;

    public string Fingerprint(IngestionItem item)
        => item.SecondaryFile is { } secondary
            ? $"{FileFingerprint(item.AbsolutePath)}|{FileFingerprint(secondary.AbsolutePath)}"
            : FileFingerprint(item.AbsolutePath);

    private string FileFingerprint(string path)
    {
        var file = fileSystem.FileInfo.New(path);
        return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "-";
    }
}
