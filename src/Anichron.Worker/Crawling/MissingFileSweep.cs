using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using System.Text.Json.Serialization;

namespace Anichron.Worker.Crawling;

/// <summary>Why a crawl soft-deleted nothing, though rows may be missing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissingFileSkip>))]
public enum MissingFileSkip
{
    /// <summary>The walk skipped a folder it could not read.</summary>
    IncompleteWalk = 0,

    /// <summary>The walk found no media files while the config has active rows, as when a share is not mounted.</summary>
    EmptyRoot = 1,
}

/// <summary>What a crawl's missing-file sweep did.</summary>
/// <param name="SoftDeleted">Rows the sweep soft-deleted.</param>
/// <param name="SkippedBecause">Why the sweep soft-deleted nothing, or null when it ran.</param>
internal sealed record MissingFiles(int SoftDeleted, MissingFileSkip? SkippedBecause);

internal interface IMissingFileSweep
{
    Task<MissingFiles> SweepAsync(UserStorageConfig config, CrawlRun run, CancellationToken ct);
}

internal sealed partial class MissingFileSweep(
    IMediaAssetRepository repository,
    ILogger<MissingFileSweep> logger) : IMissingFileSweep
{
    public async Task<MissingFiles> SweepAsync(UserStorageConfig config, CrawlRun run, CancellationToken ct)
    {
        if (!run.WalkComplete)
            return new MissingFiles(0, MissingFileSkip.IncompleteWalk);

        var active = await repository.GetActivePathsAsync(config.Id, ct);
        if (run.FoundPaths.Count == 0 && active.Count > 0)
            return new MissingFiles(0, MissingFileSkip.EmptyRoot);

        var missing = active.Where(row => !run.FoundPaths.Contains(row.FilePath)).ToList();
        if (missing.Count == 0)
            return new MissingFiles(0, null);

        foreach (var row in missing)
            Log.SoftDeleting(logger, row.Id, row.FilePath);
        return new MissingFiles(await repository.SoftDeleteAsync([.. missing.Select(row => row.Id)], ct), null);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Soft-deleting asset {AssetId} at {FilePath}: a complete crawl did not find it.")]
        public static partial void SoftDeleting(ILogger logger, Guid assetId, string filePath);
    }
}
