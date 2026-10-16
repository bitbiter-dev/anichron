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
        var missing = generators
            .Where(generator => !mov.ProxyFiles.Any(proxy => proxy.ProxyType == generator.ProxyType))
            .ToArray();
        if (missing.Length == 0)
            return false;

        var source = context.Item.SecondaryFile!.AbsolutePath;
        var directory = proxyDirectoryStrategy.GetDirectory(context.Config.Id, mov.ContentHash);
        foreach (var generator in missing)
        {
            await stagingWriter.WriteAsync(
                context,
                mov.Id,
                $"{directory}/{generator.FileName}",
                generator.ProxyType,
                (temporaryPath, token) => generator.TranscodeAsync(source, temporaryPath, token),
                ct);
        }

        foreach (var proxy in context.ProxyFiles.Where(proxy => proxy.AssetId == mov.Id))
            repository.AddProxyFile(proxy);

        Log.ProxiesGenerated(logger, missing.Length, context.Item.SecondaryFile.RelativePath);
        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} video proxy file(s) for paired {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
