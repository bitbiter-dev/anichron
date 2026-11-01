using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IUprightImageDecoder
{
    Task<Image<Rgb24>> DecodeAsync(byte[] original, CancellationToken ct);
}

internal sealed class ImageSharpUprightDecoder : IUprightImageDecoder
{
    public async Task<Image<Rgb24>> DecodeAsync(byte[] original, CancellationToken ct)
    {
        await using var ms = new MemoryStream(original);
        var image = await Image.LoadAsync<Rgb24>(ms, ct);
        image.Mutate(x => x.AutoOrient());
        return image;
    }
}
