using Anichron.Core;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.ProxySweeps;

/// <summary>What a sweep reported.</summary>
/// <param name="Orphans">Files no proxy row references, last written more than the grace period before the scan.</param>
/// <param name="Bytes">Their size in all.</param>
/// <param name="TemporaryFiles">Orphans left by an unfinished proxy write.</param>
/// <param name="ConfigsGone">Storage configs named by orphans' directories that no longer exist.</param>
public sealed record ProxySweepResult(int Orphans, long Bytes, int TemporaryFiles, int ConfigsGone);

internal sealed partial class ProxySweepHandler(
    IProxyOrphanStore store,
    IProxyDirectoryStrategy directories,
    IFileSystem fileSystem,
    IClock clock,
    ILogger<ProxySweepHandler> logger) : IWorkHandler<ProxySweepPayload>
{
    internal static Duration GracePeriod { get; } = Duration.FromHours(1);

    public async Task<object?> RunAsync(ProxySweepPayload payload, WorkRun run, CancellationToken ct)
    {
        if (!fileSystem.Directory.Exists(StoragePath.ProxiesRoot))
            throw new DirectoryNotFoundException($"The proxy root {StoragePath.ProxiesRoot} is missing.");

        var writtenBefore = clock.GetCurrentInstant() - GracePeriod;
        var atRoot = await ScanRootAsync(writtenBefore, ct);
        await store.RecordAsync(run.ItemId, atRoot, ct);

        var all = new List<FoundOrphan>(atRoot);
        var shards = fileSystem.Directory.EnumerateDirectories(StoragePath.ProxiesRoot).Order(StringComparer.Ordinal).ToList();
        for (var i = 0; i < shards.Count; i++)
        {
            run.Progress.Report(i, shards.Count, "Scanning proxy shards");
            var inShard = await ScanShardAsync(shards[i], writtenBefore, ct);
            await store.RecordAsync(run.ItemId, inShard, ct);
            all.AddRange(inShard);
        }

        run.Progress.Report(shards.Count, shards.Count, "Scanning proxy shards");
        var result = await SummariseAsync(all, ct);
        Log.Swept(logger, result.Orphans, result.Bytes, result.TemporaryFiles, result.ConfigsGone);
        return result;
    }

    private async Task<List<FoundOrphan>> ScanRootAsync(Instant writtenBefore, CancellationToken ct)
    {
        var found = new List<FoundOrphan>();
        foreach (var file in fileSystem.Directory.EnumerateFiles(StoragePath.ProxiesRoot))
        {
            var path = RelativePath(file);
            if (Unreferenced(file, path, writtenBefore) is { } orphan && !await store.IsReferencedAsync(path, ct))
                found.Add(orphan);
        }

        return found;
    }

    private async Task<List<FoundOrphan>> ScanShardAsync(string shard, Instant writtenBefore, CancellationToken ct)
    {
        var referenced = await store.ReferencedUnderAsync(fileSystem.Path.GetFileName(shard), ct);
        return [.. FilesUnder(shard)
            .Select(file => (File: file, Path: RelativePath(file)))
            .Where(file => !referenced.Contains(file.Path))
            .Select(file => Unreferenced(file.File, file.Path, writtenBefore))
            .OfType<FoundOrphan>()];
    }

    private IEnumerable<string> FilesUnder(string directory)
        => fileSystem.Directory.EnumerateFiles(directory)
            .Concat(fileSystem.Directory.EnumerateDirectories(directory).SelectMany(FilesUnder));

    private FoundOrphan? Unreferenced(string file, string path, Instant writtenBefore)
    {
        var stamp = ProxyFileStamp.Of(fileSystem.FileInfo.New(file));
        return stamp.LastWriteTime < writtenBefore
            ? new FoundOrphan(path, directories.StorageConfigOf(path), stamp.SizeBytes, stamp.LastWriteTime)
            : null;
    }

    private async Task<ProxySweepResult> SummariseAsync(List<FoundOrphan> found, CancellationToken ct)
    {
        var named = found.Select(o => o.StorageConfigId).OfType<Guid>().ToHashSet();
        var existing = await store.ExistingConfigsAsync(named, ct);
        return new ProxySweepResult(
            found.Count,
            found.Sum(o => o.SizeBytes),
            found.Count(o => o.ProxyPath.EndsWith(ProxyStagingWriter.TemporaryFileSuffix, StringComparison.Ordinal)),
            named.Count(id => !existing.Contains(id)));
    }

    private string RelativePath(string file) => fileSystem.Path.GetRelativePath(StoragePath.ProxiesRoot, file);

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Swept the proxy root: {Orphans} orphan(s), {Bytes} B, {TemporaryFiles} temporary file(s), {ConfigsGone} storage config(s) gone.")]
        public static partial void Swept(ILogger logger, int orphans, long bytes, int temporaryFiles, int configsGone);
    }
}
