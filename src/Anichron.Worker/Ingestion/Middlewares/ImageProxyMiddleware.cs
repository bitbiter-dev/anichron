using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ImageProxyMiddleware(
    IEnumerable<IImageProxyGenerator> generators,
    IProxyDirectoryStrategy proxyDirectoryStrategy,
    IOptions<WorkerSettings> settings,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<ImageProxyMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.ImageProxy;
    public bool CanInvoke(IngestionContext context)
        => context.Item.PrimaryMediaType is MediaType.Image or MediaType.LivePhoto;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var proxyRoot = settings.Value.ProxyPath;
        var proxyDirectoryName = proxyDirectoryStrategy.GetDirectory(context.AssetId);
        var proxyPath = Path.Combine(proxyRoot, proxyDirectoryName);
        var sourceBytes = await fileSystem.File.ReadAllBytesAsync(context.Item.AbsolutePath, ct);

        fileSystem.Directory.CreateDirectory(proxyPath);

        var proxyFiles = await Task.WhenAll(generators.Select(WriteProxyAsync));

        context.ProxyFiles.AddRange(proxyFiles);

        Log.ProxiesGenerated(logger, proxyFiles.Length, context.Item.RelativePath);
        await next(context, ct);

        async Task<ProxyFile> WriteProxyAsync(IImageProxyGenerator generator)
        {
            var relativePath = $"{proxyDirectoryName}/{generator.FileName}";
            await using var ms = new MemoryStream(sourceBytes);
            var bytes = await generator.GenerateAsync(ms, ct);
            await fileSystem.File.WriteAllBytesAsync(Path.Combine(proxyRoot, relativePath), bytes, ct);
            Log.ProxyWritten(logger, generator.ProxyType, bytes.Length, context.Item.RelativePath);
            return ProxyFileBuilder.Build(context, relativePath, generator.ProxyType, bytes.Length, guidFactory, clock);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote {ProxyType} proxy ({SizeBytes} B) for {RelativePath}.")]
        public static partial void ProxyWritten(ILogger logger, ProxyType proxyType, long sizeBytes, string relativePath);

        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} proxy files for {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
