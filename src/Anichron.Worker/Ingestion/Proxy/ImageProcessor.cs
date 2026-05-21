using Anichron.Worker.Settings;
using Blurhash.ImageSharp;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IImageProcessor
{
    Task<byte[]> CreateThumbnailAsync(Image<Rgba32> image, CancellationToken ct);
    Task<byte[]> CreateFullPreviewAsync(Image<Rgba32> image, CancellationToken ct);
    Task<string> ComputeBlurhashAsync(Image<Rgba32> image, CancellationToken ct);
}

internal sealed class ImageSharpProcessor(IOptions<WorkerSettings> options) : IImageProcessor
{
    private readonly WorkerSettings settings = options.Value;

    public async Task<byte[]> CreateThumbnailAsync(Image<Rgba32> image, CancellationToken ct)
    {
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(settings.ThumbnailMaxWidth, 0),
            Mode = ResizeMode.Max,
        }));

        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = settings.ThumbnailJpegQuality }, ct);
        return ms.ToArray();
    }

    public async Task<byte[]> CreateFullPreviewAsync(Image<Rgba32> image, CancellationToken ct)
    {
        if (image.Width > settings.PreviewMaxWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(settings.PreviewMaxWidth, 0),
                Mode = ResizeMode.Max,
            }));
        }

        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = settings.PreviewJpegQuality }, ct);
        return ms.ToArray();
    }

    public Task<string> ComputeBlurhashAsync(Image<Rgba32> image, CancellationToken ct)
    {
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(settings.BlurhashSampleWidth, 0),
            Mode = ResizeMode.Max,
        }));

        // Blurhasher.Encode is CPU-bound and synchronous; no overload accepts a CancellationToken.
        return Task.FromResult(Blurhasher.Encode(image, 4, 3));
    }
}
