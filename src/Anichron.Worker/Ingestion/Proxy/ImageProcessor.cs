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
    Task<byte[]> CreateThumbnailAsync(Stream source, CancellationToken ct);
    Task<byte[]> CreateFullPreviewAsync(Stream source, CancellationToken ct);
    Task<string> ComputeBlurhashAsync(Stream source, CancellationToken ct);
}

internal sealed class ImageSharpProcessor(IOptions<WorkerSettings> options) : IImageProcessor
{
    public async Task<byte[]> CreateThumbnailAsync(Stream source, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(source, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(options.Value.ThumbnailMaxWidth, 0),
            Mode = ResizeMode.Max,
        }));

        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = options.Value.ThumbnailJpegQuality }, ct);
        return ms.ToArray();
    }

    public async Task<byte[]> CreateFullPreviewAsync(Stream source, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(source, ct);
        if (image.Width > options.Value.PreviewMaxWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(options.Value.PreviewMaxWidth, 0),
                Mode = ResizeMode.Max,
            }));
        }

        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = options.Value.PreviewJpegQuality }, ct);
        return ms.ToArray();
    }

    public async Task<string> ComputeBlurhashAsync(Stream source, CancellationToken ct)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(options.Value.BlurhashSampleWidth, 0),
            Mode = ResizeMode.Max,
        }));

        return Blurhasher.Encode(image, 4, 3);
    }
}
