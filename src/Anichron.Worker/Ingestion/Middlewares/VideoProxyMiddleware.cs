using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class VideoProxyMiddleware(
    IEnumerable<IVideoProxyGenerator> generators,
    IProxyDirectoryStrategy proxyDirectoryStrategy,
    ProxyStagingWriter stagingWriter,
    ILogger<VideoProxyMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.VideoProxy;
    public bool CanInvoke(IngestionContext context)
        => context.Item.PrimaryMediaType == MediaType.Video;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first; suppression is safe.
        var proxyDirectoryName = proxyDirectoryStrategy.GetDirectory(context.Config.Id, context.ContentHash!);

        // Sequential: concurrent FFmpeg processes would saturate the GPU.
        var pending = generators.ToArray();
        foreach (var generator in pending)
            await ProcessGeneratorAsync(generator);

        Log.ProxiesGenerated(logger, pending.Length, context.Item.RelativePath);
        await next(context, ct);

        Task ProcessGeneratorAsync(IVideoProxyGenerator generator)
            => stagingWriter.WriteAsync(
                context,
                $"{proxyDirectoryName}/{generator.FileName}",
                generator.ProxyType,
                (temporaryPath, token) => generator.TranscodeAsync(context.Item.AbsolutePath, temporaryPath, token),
                ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} video proxy file(s) for {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
