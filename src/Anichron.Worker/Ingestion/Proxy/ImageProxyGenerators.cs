using Anichron.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace Anichron.Worker.Ingestion.Proxy;

internal static class ImageProxyOrder
{
    public const int FullPreview = 10;
    public const int Thumbnail = 20;
    public const int Blurhash = 30;
}

internal interface IImageProxyGenerator
{
    string FileName { get; }
    ProxyType ProxyType { get; }

    /// <summary>
    /// Position in the downscale chain. Generators run in ascending order on one shared image, and each
    /// may shrink it in place, so a generator must not need more pixels than the one before it left.
    /// </summary>
    int Order { get; }

    Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct);
}

internal sealed class ThumbnailGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "thumbnail.jpg";
    public ProxyType ProxyType => ProxyType.Thumbnail;
    public int Order => ImageProxyOrder.Thumbnail;
    public Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct)
        => imageProcessor.CreateThumbnailAsync(image, ct);
}

internal sealed class FullPreviewGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "preview.jpg";
    public ProxyType ProxyType => ProxyType.FullPreview;
    public int Order => ImageProxyOrder.FullPreview;
    public Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct)
        => imageProcessor.CreateFullPreviewAsync(image, ct);
}

internal sealed class BlurhashGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "blurhash.txt";
    public ProxyType ProxyType => ProxyType.BlurHash;
    public int Order => ImageProxyOrder.Blurhash;
    public async Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct)
    {
        var hash = await imageProcessor.ComputeBlurhashAsync(image, ct);
        return Encoding.UTF8.GetBytes(hash);
    }
}
