using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.ProxySweeps;

public sealed record ProxyDeletionResult(int Deleted, int SkippedReferenced, int SkippedChanged, int Missing, int Failed)
{
    public static ProxyDeletionResult None { get; } = new(0, 0, 0, 0, 0);

    public ProxyDeletionResult Counting(ProxyDeletionOutcome outcome) => outcome switch
    {
        ProxyDeletionOutcome.Deleted => this with { Deleted = Deleted + 1 },
        ProxyDeletionOutcome.SkippedReferenced => this with { SkippedReferenced = SkippedReferenced + 1 },
        ProxyDeletionOutcome.SkippedChanged => this with { SkippedChanged = SkippedChanged + 1 },
        ProxyDeletionOutcome.Missing => this with { Missing = Missing + 1 },
        ProxyDeletionOutcome.Failed => this with { Failed = Failed + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a deletion outcome."),
    };
}

internal sealed partial class ProxyDeletionHandler(
    IProxyOrphanStore store,
    IUnitOfWork unitOfWork,
    IProxyDirectoryStrategy directories,
    IFileSystem fileSystem,
    IClock clock,
    ILogger<ProxyDeletionHandler> logger) : IWorkHandler<ProxyDeletionPayload>
{
    internal const int BatchSize = 500;

    public async Task<object?> RunAsync(ProxyDeletionPayload payload, WorkRun run, CancellationToken ct)
    {
        var result = ProxyDeletionResult.None;
        var done = 0;
        while (await store.GetPendingAsync(payload, BatchSize, ct) is { Count: > 0 } batch)
        {
            foreach (var orphan in batch)
            {
                var outcome = await DeleteAsync(orphan, ct);
                orphan.DeletionOutcome = outcome;
                orphan.ProcessedAt = clock.GetCurrentInstant();
                await unitOfWork.SaveChangesAsync(ct);
                result = result.Counting(outcome);
                run.Progress.Report(++done, note: "Deleting orphaned proxy files");
            }

            store.ForgetLoaded();
        }

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
        if (directories.ContentDirectoryOf(proxyPath) is not { } content)
            return;

        var directory = fileSystem.Path.Combine(StoragePath.ProxiesRoot, content);
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
