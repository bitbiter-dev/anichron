using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Ingestion.Proxy;

internal sealed class MagickHeifDecoder : IUprightImageDecoder
{
    public Task<Image<Rgb24>> DecodeAsync(byte[] original, CancellationToken ct)
    {
        using var heif = new MagickImage(original, MagickFormat.Heic);
        using var pixels = heif.GetPixelsUnsafe();
        var rgb = pixels.ToByteArray(PixelMapping.RGB);
        return Task.FromResult(Image.LoadPixelData<Rgb24>(rgb, (int)heif.Width, (int)heif.Height));
    }
}
