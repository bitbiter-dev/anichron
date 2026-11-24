using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Ingestion.Proxy;

internal sealed class MagickHeifDecoder : IUprightImageDecoder
{
    public Task<Image<Rgb24>> DecodeAsync(byte[] original, CancellationToken ct)
    {
        using var heif = Read(original);
        using var pixels = heif.GetPixelsUnsafe();
        var image = Image.LoadPixelData<Rgb24>(pixels.ToByteArray(PixelMapping.RGB), (int)heif.Width, (int)heif.Height);
        if (heif.GetColorProfile() is { } profile)
            image.Metadata.IccProfile = new IccProfile(profile.ToByteArray());
        return Task.FromResult(image);
    }

    private static MagickImage Read(byte[] original)
    {
        try
        {
            return new MagickImage(original, MagickFormat.Heic);
        }
        catch (MagickCorruptImageErrorException ex)
        {
            throw new InvalidImageContentException(ex.Message, ex);
        }
    }
}
