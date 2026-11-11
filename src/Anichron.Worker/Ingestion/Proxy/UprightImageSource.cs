using Anichron.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IUprightImageSource
{
    Task<Image<Rgb24>> LoadAsync(IngestionItem item, CancellationToken ct);
}

internal sealed class UprightImageSource(
    IUprightImageDecoder decoder,
    IVideoFrameExtractor frameExtractor,
    IFileSystem fileSystem) : IUprightImageSource
{
    public async Task<Image<Rgb24>> LoadAsync(IngestionItem item, CancellationToken ct)
    {
        if (item.PrimaryMediaType == MediaType.Video)
            return await frameExtractor.ExtractAsync(item.AbsolutePath, ct);
        return await decoder.DecodeAsync(await fileSystem.File.ReadAllBytesAsync(item.AbsolutePath, ct), ct);
    }
}
