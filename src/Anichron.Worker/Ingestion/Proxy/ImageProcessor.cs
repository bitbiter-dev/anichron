using Anichron.Worker.Settings;
using Blurhash.ImageSharp;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IImageProcessor
{
    Task<byte[]> EncodeThumbnailAsync(Image<Rgb24> image, CancellationToken ct);
    Task<byte[]> EncodeFullPreviewAsync(Image<Rgb24> image, CancellationToken ct);
    Task<string> ComputeBlurhashAsync(Image<Rgb24> image, CancellationToken ct);
}

internal sealed class ImageSharpProcessor(IOptions<WorkerSettings> options) : IImageProcessor
{
    private readonly WorkerSettings settings = options.Value;

    public Task<byte[]> EncodeThumbnailAsync(Image<Rgb24> image, CancellationToken ct)
        => EncodeJpegAsync(image, settings.ThumbnailJpegQuality, ct);

    public Task<byte[]> EncodeFullPreviewAsync(Image<Rgb24> image, CancellationToken ct)
        => EncodeJpegAsync(image, settings.PreviewJpegQuality, ct);

    // Blurhasher.Encode is CPU-bound and synchronous; no overload accepts a CancellationToken.
    public Task<string> ComputeBlurhashAsync(Image<Rgb24> image, CancellationToken ct)
        => Task.FromResult(Blurhasher.Encode(image, 4, 3));

    private static async Task<byte[]> EncodeJpegAsync(Image<Rgb24> image, int quality, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality }, ct);
        return ms.ToArray();
    }
}
