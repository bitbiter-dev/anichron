using Anichron.Core.Domain;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IImageProxyGenerator
{
    string FileName { get; }
    ProxyType ProxyType { get; }

    /// <summary>
    /// The most pixels this generator uses. It receives its own copy of the upright source, already shrunk
    /// to fit this bound, and may modify that copy freely.
    /// </summary>
    ProxyBound Bound { get; }

    Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct);
}

internal sealed class ThumbnailGenerator(IImageProcessor imageProcessor, IOptions<WorkerSettings> options)
    : IImageProxyGenerator
{
    public string FileName => "thumbnail.jpg";
    public ProxyType ProxyType => ProxyType.Thumbnail;
    public ProxyBound Bound => new(ProxyEdge.ShortEdge, options.Value.ThumbnailShortEdge);
    public Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct)
        => imageProcessor.EncodeThumbnailAsync(image, ct);
}

internal sealed class FullPreviewGenerator(IImageProcessor imageProcessor, IOptions<WorkerSettings> options)
    : IImageProxyGenerator
{
    public string FileName => "preview.jpg";
    public ProxyType ProxyType => ProxyType.FullPreview;
    public ProxyBound Bound => new(ProxyEdge.LongEdge, options.Value.PreviewLongEdge);
    public Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct)
        => imageProcessor.EncodeFullPreviewAsync(image, ct);
}

internal sealed class BlurhashEncoder(IImageProcessor imageProcessor, IOptions<WorkerSettings> options)
{
    public ProxyBound Bound => new(ProxyEdge.LongEdge, options.Value.BlurhashSampleLongEdge);
    public Task<string> EncodeAsync(Image<Rgb24> image, CancellationToken ct)
        => imageProcessor.ComputeBlurhashAsync(image, ct);
}
