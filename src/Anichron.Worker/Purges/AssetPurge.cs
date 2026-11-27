using Anichron.Core;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Work;

namespace Anichron.Worker.Purges;

/// <summary>What a purge did.</summary>
/// <param name="Purged">Rows deleted.</param>
/// <param name="SkippedRestored">Rows kept because they were restored since the request.</param>
/// <param name="AlreadyGone">Rows no longer there, as on a retry.</param>
/// <param name="ProxyFilesDeleted">Proxy files of purged rows deleted, or found already gone.</param>
/// <param name="ProxyFilesKept">Proxy files kept because another row references them.</param>
/// <param name="ProxyFilesFailed">Proxy files that could not be deleted, left for the proxy sweep.</param>
public sealed record AssetPurgeResult(
    int Purged, int SkippedRestored, int AlreadyGone, int ProxyFilesDeleted, int ProxyFilesKept, int ProxyFilesFailed);

internal sealed partial class AssetPurgeHandler(
    ActiveStorageConfigs activeConfigs,
    IAssetPurgeStore store,
    ProxyFileRemover remover,
    ILogger<AssetPurgeHandler> logger) : IWorkHandler<AssetPurgePayload>
{
    internal const int BatchSize = 500;

    public async Task<object?> RunAsync(AssetPurgePayload payload, WorkRun run, CancellationToken ct)
    {
        if (await activeConfigs.FindAsync(AssetPurgePayload.Kind, payload.StorageConfigId, ct) is null)
            return StorageConfigSkipped.MissingOrInactive;

        var tally = new Tally();
        var done = 0;
        foreach (var batch in payload.AssetIds.Chunk(BatchSize))
        {
            var rows = await store.DeleteSoftDeletedAsync(payload.StorageConfigId, batch, ct);
            tally.Purged += rows.Select(row => row.AssetId).Distinct().Count();
            tally.Restored += await store.CountPresentAsync(payload.StorageConfigId, batch, ct);
            foreach (var path in rows.Select(row => row.ProxyPath).OfType<string>().Distinct(StringComparer.Ordinal))
                await RemoveAsync(path, tally, ct);

            done += batch.Length;
            run.Progress.Report(done, payload.AssetIds.Count, "Purging soft-deleted assets");
        }

        var result = new AssetPurgeResult(
            tally.Purged, tally.Restored, payload.AssetIds.Count - tally.Purged - tally.Restored, tally.Deleted, tally.Kept, tally.Failed);
        Log.Finished(
            logger, payload.StorageConfigId, result.Purged, result.SkippedRestored, result.AlreadyGone,
            result.ProxyFilesDeleted, result.ProxyFilesKept, result.ProxyFilesFailed);
        return result;
    }

    private async Task RemoveAsync(string proxyPath, Tally tally, CancellationToken ct)
    {
        if (StoragePath.ResolveProxy(proxyPath) is not { } file)
        {
            Log.OutsideRoot(logger, proxyPath);
            tally.Failed++;
        }
        else if (await store.IsReferencedAsync(proxyPath, ct))
        {
            tally.Kept++;
        }
        else if (remover.Delete(proxyPath, file))
        {
            tally.Deleted++;
        }
        else
        {
            tally.Failed++;
        }
    }

    private sealed class Tally
    {
        public int Purged { get; set; }
        public int Restored { get; set; }
        public int Deleted { get; set; }
        public int Kept { get; set; }
        public int Failed { get; set; }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Purged soft-deleted assets of storage config {StorageConfigId}: {Purged} purged, {Restored} restored, "
                + "{AlreadyGone} already gone; proxy files: {Deleted} deleted, {Kept} kept, {Failed} failed.")]
        public static partial void Finished(
            ILogger logger, Guid storageConfigId, int purged, int restored, int alreadyGone, int deleted, int kept, int failed);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Did not delete proxy file {ProxyPath} of a purged asset: it resolves outside the proxy root.")]
        public static partial void OutsideRoot(ILogger logger, string proxyPath);
    }
}
