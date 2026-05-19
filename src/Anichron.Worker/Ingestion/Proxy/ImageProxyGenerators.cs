using Anichron.Core.Domain;
using System.Text;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IImageProxyGenerator
{
    string FileName { get; }
    ProxyType ProxyType { get; }
    Task<byte[]> GenerateAsync(Stream source, CancellationToken ct);
}

internal sealed class ThumbnailGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "thumbnail.jpg";
    public ProxyType ProxyType => ProxyType.Thumbnail;
    public Task<byte[]> GenerateAsync(Stream source, CancellationToken ct)
        => imageProcessor.CreateThumbnailAsync(source, ct);
}

internal sealed class FullPreviewGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "preview.jpg";
    public ProxyType ProxyType => ProxyType.FullPreview;
    public Task<byte[]> GenerateAsync(Stream source, CancellationToken ct)
        => imageProcessor.CreateFullPreviewAsync(source, ct);
}

internal sealed class BlurhashGenerator(IImageProcessor imageProcessor) : IImageProxyGenerator
{
    public string FileName => "blurhash.txt";
    public ProxyType ProxyType => ProxyType.BlurHash;
    public async Task<byte[]> GenerateAsync(Stream source, CancellationToken ct)
    {
        var hash = await imageProcessor.ComputeBlurhashAsync(source, ct);
        return Encoding.UTF8.GetBytes(hash);
    }
}
