using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Proxy;

internal sealed partial class PairedMovProxyWriter(
    IEnumerable<IVideoProxyGenerator> generators,
    IProxyDirectoryStrategy proxyDirectoryStrategy,
    ProxyStagingWriter stagingWriter,
    IMediaAssetRepository repository,
    ILogger<PairedMovProxyWriter> logger)
{
    internal async Task<bool> WriteMissingAsync(IngestionContext context, MediaAsset mov, CancellationToken ct)
    {
        var written = context.ProxyFilesOf(mov.Id);
        var missing = generators
            .Where(generator => !mov.ProxyFiles.Concat(written).Any(proxy => proxy.ProxyType == generator.ProxyType))
            .ToArray();
        if (missing.Length == 0)
            return false;

        await WriteAsync(context, mov, mov.ContentHash, missing, ct);
        repository.AddProxyFiles(context.ProxyFilesOf(mov.Id));
        return true;
    }

    /// <summary>Writes every video proxy of the item's video into its content's folder, for <paramref name="mov"/> to take.</summary>
    /// <param name="context">The item whose video changed content.</param>
    /// <param name="mov">The row that takes the content; it owns the proxies.</param>
    /// <param name="ct">Cancels the transcode.</param>
    /// <returns>The written proxies, not added to the repository.</returns>
    internal async Task<IReadOnlyList<ProxyFile>> WriteForNewContentAsync(IngestionContext context, MediaAsset mov, CancellationToken ct)
    {
        await WriteAsync(context, mov, context.SecondaryHash!, [.. generators], ct);
        return context.ProxyFilesOf(mov.Id);
    }

    private async Task WriteAsync(
        IngestionContext context, MediaAsset mov, string contentHash, IVideoProxyGenerator[] pending, CancellationToken ct)
    {
        var source = context.Item.SecondaryFile!.AbsolutePath;
        var directory = proxyDirectoryStrategy.GetDirectory(context.Config.Id, contentHash);
        foreach (var generator in pending)
        {
            await stagingWriter.WriteAsync(
                context,
                mov.Id,
                $"{directory}/{generator.FileName}",
                generator.ProxyType,
                (temporaryPath, token) => generator.TranscodeAsync(source, temporaryPath, token),
                ct);
        }

        context.StageMovProxies(mov.Id);
        Log.ProxiesGenerated(logger, pending.Length, context.Item.SecondaryFile.RelativePath);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} video proxy file(s) for paired {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
