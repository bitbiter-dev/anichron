using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ImageProxyMiddleware(
    IEnumerable<IImageProxyGenerator> generators,
    BlurhashEncoder blurhashEncoder,
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
        using var image = await Image.LoadAsync<Rgb24>(ms, ct);
        image.Mutate(x => x.AutoOrient());

        var proxySteps = generators
            .Select(g => new Step(g.Bound, () => WriteProxyAsync(g)))
            .ToArray();
        Step[] allSteps = [.. proxySteps, new Step(blurhashEncoder.Bound, EncodeBlurhashAsync)];
        var steps = allSteps
            .Select(s => (Step: s, Target: s.Bound.FitTo(image.Size)))
            .OrderByDescending(s => (long)s.Target.Width * s.Target.Height)
            .ToArray();
        foreach (var (step, target) in steps)
        {
            if (image.Size != target)
                image.Mutate(x => x.Resize(target));
            await step.RunAsync();
        }

        Log.ProxiesGenerated(logger, proxySteps.Length, context.Item.RelativePath);
        await next(context, ct);

        Task WriteProxyAsync(IImageProxyGenerator generator)
        {
            return stagingWriter.WriteAsync(
                context,
                context.AssetId,
                $"{proxyDirectoryName}/{generator.FileName}",
                generator.ProxyType,
                ProduceAsync,
                ct);

            async Task ProduceAsync(string temporaryPath, CancellationToken token)
            {
                using var copy = image.Clone();
                var bytes = await generator.GenerateAsync(copy, token);
                await fileSystem.File.WriteAllBytesAsync(temporaryPath, bytes, token);
            }
        }

        async Task EncodeBlurhashAsync()
        {
            using var copy = image.Clone();
            context.Blurhash = await blurhashEncoder.EncodeAsync(copy, ct);
        }
    }

    private sealed record Step(ProxyBound Bound, Func<Task> RunAsync);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} proxy files for {RelativePath}.")]
        public static partial void ProxiesGenerated(ILogger logger, int count, string relativePath);
    }
}
