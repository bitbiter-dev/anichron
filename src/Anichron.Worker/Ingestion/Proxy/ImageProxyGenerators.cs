using Anichron.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IImageProxyGenerator
{
    string FileName { get; }
    ProxyType ProxyType { get; }
    Task<byte[]> GenerateAsync(Image<Rgba32> image, CancellationToken ct);
}

internal sealed class ThumbnailGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "thumbnail.jpg";
    public ProxyType ProxyType => ProxyType.Thumbnail;
    public Task<byte[]> GenerateAsync(Image<Rgba32> image, CancellationToken ct)
        => imageProcessor.CreateThumbnailAsync(image, ct);
}

internal sealed class FullPreviewGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "preview.jpg";
    public ProxyType ProxyType => ProxyType.FullPreview;
    public Task<byte[]> GenerateAsync(Image<Rgba32> image, CancellationToken ct)
        => imageProcessor.CreateFullPreviewAsync(image, ct);
}

internal sealed class BlurhashGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "blurhash.txt";
    public ProxyType ProxyType => ProxyType.BlurHash;
    public async Task<byte[]> GenerateAsync(Image<Rgba32> image, CancellationToken ct)
    {
        var hash = await imageProcessor.ComputeBlurhashAsync(image, ct);
        return Encoding.UTF8.GetBytes(hash);
    }
}
