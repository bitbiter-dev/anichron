using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.ProxySweeps;

public sealed record ProxyDeletionResult(int Deleted, int SkippedReferenced, int SkippedChanged, int Missing, int Failed);

internal sealed partial class ProxyDeletionHandler(
    IProxyOrphanStore store,
    IFileSystem fileSystem,
    IClock clock,
    ILogger<ProxyDeletionHandler> logger) : IWorkHandler<ProxyDeletionPayload>
{
    internal const int BatchSize = 500;

    public async Task<object?> RunAsync(ProxyDeletionPayload payload, WorkRun run, CancellationToken ct)
    {
        var outcomes = new Dictionary<ProxyDeletionOutcome, int>();
        var done = 0;
        while (await store.GetPendingAsync(payload, BatchSize, ct) is { Count: > 0 } batch)
        {
            foreach (var orphan in batch)
            {
                var outcome = await DeleteAsync(orphan, ct);
                orphan.DeletionOutcome = outcome;
                orphan.ProcessedAt = clock.GetCurrentInstant();
                await store.SaveChangesAsync(ct);
                outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
                run.Progress.Report(++done, note: "Deleting orphaned proxy files");
            }

            store.ForgetLoaded();
        }

        var result = new ProxyDeletionResult(
            outcomes.GetValueOrDefault(ProxyDeletionOutcome.Deleted),
            outcomes.GetValueOrDefault(ProxyDeletionOutcome.SkippedReferenced),
            outcomes.GetValueOrDefault(ProxyDeletionOutcome.SkippedChanged),
            outcomes.GetValueOrDefault(ProxyDeletionOutcome.Missing),
            outcomes.GetValueOrDefault(ProxyDeletionOutcome.Failed));
        Log.Finished(logger, payload.SweepId, result.Deleted, result.SkippedReferenced, result.SkippedChanged, result.Missing, result.Failed);
        return result;
    }

    private async Task<ProxyDeletionOutcome> DeleteAsync(ProxyOrphan orphan, CancellationToken ct)
    {
        if (await store.IsReferencedAsync(orphan.ProxyPath, ct))
            return ProxyDeletionOutcome.SkippedReferenced;

        var file = fileSystem.Path.Combine(StoragePath.ProxiesRoot, orphan.ProxyPath);
        if (!fileSystem.File.Exists(file))
            return ProxyDeletionOutcome.Missing;
        if (ProxyFileStamp.Of(fileSystem.FileInfo.New(file)) != new ProxyFileStamp(orphan.SizeBytes, orphan.LastWriteTime))
            return ProxyDeletionOutcome.SkippedChanged;

        try
        {
            fileSystem.File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DeleteFailed(logger, orphan.ProxyPath, ex);
            return ProxyDeletionOutcome.Failed;
        }

        RemoveContentDirectoryIfEmpty(orphan.ProxyPath);
        return ProxyDeletionOutcome.Deleted;
    }

    private void RemoveContentDirectoryIfEmpty(string proxyPath)
    {
        if (proxyPath.Split('/') is not [var shard, var content, _])
            return;

        var directory = fileSystem.Path.Combine(StoragePath.ProxiesRoot, shard, content);
        try
        {
            if (!fileSystem.Directory.EnumerateFileSystemEntries(directory).Any())
                fileSystem.Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DirectoryKept(logger, directory, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Deleted orphaned proxy files of sweep {SweepId}: {Deleted} deleted, {SkippedReferenced} referenced, "
                + "{SkippedChanged} changed, {Missing} missing, {Failed} failed.")]
        public static partial void Finished(
            ILogger logger, Guid sweepId, int deleted, int skippedReferenced, int skippedChanged, int missing, int failed);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete orphaned proxy file {ProxyPath}.")]
        public static partial void DeleteFailed(ILogger logger, string proxyPath, Exception ex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Kept proxy directory {Directory}: it could not be removed.")]
        public static partial void DirectoryKept(ILogger logger, string directory, Exception ex);
    }
}
