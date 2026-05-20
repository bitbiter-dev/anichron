using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class VideoProxyMiddleware(
    IEnumerable<IVideoProxyGenerator> generators,
    IProxyDirectoryStrategy proxyDirectoryStrategy,
    IOptions<WorkerSettings> settings,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<VideoProxyMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.VideoProxy;
    public bool CanInvoke(IngestionContext context)
        => context.Item.PrimaryMediaType == MediaType.Video;

    // Same as ImageProxyMiddleware — skips redundant CreateDirectory calls.
    private readonly HashSet<string> createdDirectories = [];
    private readonly Lock directoryLock = new();

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        var proxyRoot = settings.Value.ProxyPath;
        var proxyDirectoryName = proxyDirectoryStrategy.GetDirectory(context.AssetId);
        var proxyPath = Path.Combine(proxyRoot, proxyDirectoryName);

        lock (directoryLock)
        {
            if (createdDirectories.Add(proxyPath))
                fileSystem.Directory.CreateDirectory(proxyPath);
        }

        // Sequential: concurrent FFmpeg processes would saturate the GPU.
        var proxyFiles = new List<ProxyFile>();
        foreach (var generator in generators)
            proxyFiles.Add(await TranscodeAsync(generator));

        context.ProxyFiles.AddRange(proxyFiles);

        Log.ProxiesGenerated(logger, proxyFiles.Count, context.Item.RelativePath);
        await next(context, ct);
        return;

        async Task<ProxyFile> TranscodeAsync(IVideoProxyGenerator generator)
        {
            var relativePath = $"{proxyDirectoryName}/{generator.FileName}";
            var outputAbsolutePath = Path.Combine(proxyRoot, relativePath);
            await generator.TranscodeAsync(context.Item.AbsolutePath, outputAbsolutePath, ct);
            var sizeBytes = fileSystem.FileInfo.New(outputAbsolutePath).Length;
            Log.ProxyWritten(logger, generator.ProxyType, sizeBytes, context.Item.RelativePath);
            return ProxyFileBuilder.Build(context, relativePath, generator.ProxyType, sizeBytes, guidFactory, clock);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Transcoded {ProxyType} proxy ({SizeBytes} B) for {RelativePath}.")]
        public static partial void ProxyWritten(ILogger logger, ProxyType proxyType, long sizeBytes, string relativePath);

        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} video proxy file(s) for {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
