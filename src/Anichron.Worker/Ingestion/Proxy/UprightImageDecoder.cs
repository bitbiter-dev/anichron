using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IUprightImageDecoder
{
    Task<Image<Rgb24>> DecodeAsync(byte[] original, CancellationToken ct);
}

internal sealed class UprightImageDecoder(MagickHeifDecoder heif, ImageSharpUprightDecoder imageSharp) : IUprightImageDecoder
{
    public Task<Image<Rgb24>> DecodeAsync(byte[] original, CancellationToken ct)
        => IsHeifStill(original) ? heif.DecodeAsync(original, ct) : imageSharp.DecodeAsync(original, ct);

    private static bool IsHeifStill(byte[] original)
        => original is [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', .. var brand]
           && brand is [(byte)'h', (byte)'e', (byte)'i', (byte)'c', ..]
               or [(byte)'h', (byte)'e', (byte)'i', (byte)'x', ..]
               or [(byte)'m', (byte)'i', (byte)'f', (byte)'1', ..];
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
