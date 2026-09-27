using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ImageProxyMiddleware(
    IEnumerable<IImageProxyGenerator> generators,
    IProxyDirectoryStrategy proxyDirectoryStrategy,
    ProxyStagingWriter stagingWriter,
    IFileSystem fileSystem,
    ILogger<ImageProxyMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.ImageProxy;
    public bool CanInvoke(IngestionContext context)
        => context.Item.PrimaryMediaType is MediaType.Image or MediaType.LivePhoto;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first; suppression is safe.
        var proxyDirectoryName = proxyDirectoryStrategy.GetDirectory(context.Config.Id, context.ContentHash!);
        var sourceBytes = await fileSystem.File.ReadAllBytesAsync(context.Item.AbsolutePath, ct);

        await using var ms = new MemoryStream(sourceBytes);
        using var image = await Image.LoadAsync<Rgba32>(ms, ct);

        var writes = generators.Select(WriteProxyAsync).ToArray();
        await Task.WhenAll(writes);

        Log.ProxiesGenerated(logger, writes.Length, context.Item.RelativePath);
        await next(context, ct);

        Task WriteProxyAsync(IImageProxyGenerator generator)
        {
            return stagingWriter.WriteAsync(
                context,
                $"{proxyDirectoryName}/{generator.FileName}",
                generator.ProxyType,
                ProduceAsync,
                ct);

            async Task ProduceAsync(string temporaryPath, CancellationToken token)
            {
                using var clone = image.Clone();
                var bytes = await generator.GenerateAsync(clone, token);
                await fileSystem.File.WriteAllBytesAsync(temporaryPath, bytes, token);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} proxy files for {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
