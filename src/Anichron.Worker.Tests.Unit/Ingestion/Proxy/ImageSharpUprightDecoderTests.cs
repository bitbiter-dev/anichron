using Anichron.Worker.Ingestion.Proxy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ImageSharpUprightDecoderTests
{
    private static byte[] JpegWithOrientation(ushort orientation)
    {
        using var img = new Image<Rgb24>(40, 20, Color.Black.ToPixel<Rgb24>());
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < 10; y++)
                rows.GetRowSpan(y)[..20].Fill(Color.Red.ToPixel<Rgb24>());
        });
        img.Metadata.ExifProfile = new ExifProfile();
        img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task DecodeAsync_JpegWithOrientation6_ReturnsTheImageRotatedUprightAsync()
    {
        using var image = await new ImageSharpUprightDecoder().DecodeAsync(
            JpegWithOrientation(ExifOrientationMode.RightTop), TestContext.Current.CancellationToken);

        image.Size.Should().Be(new Size(20, 40));
        image[15, 5].R.Should().BeGreaterThan(128, "the stored top-left corner turns to the top right");
        image[5, 5].R.Should().BeLessThan(128);
    }

    [Fact]
    public async Task DecodeAsync_JpegWithOrientation6_ResetsTheOrientationTagAsync()
    {
        using var image = await new ImageSharpUprightDecoder().DecodeAsync(
            JpegWithOrientation(ExifOrientationMode.RightTop), TestContext.Current.CancellationToken);

        var tag = image.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var value) == true
            ? value.Value
            : ExifOrientationMode.TopLeft;
        tag.Should().Be(ExifOrientationMode.TopLeft);
    }

    [Fact]
    public async Task DecodeAsync_BytesNoDecoderReads_ThrowsAsync()
    {
        var act = () => new ImageSharpUprightDecoder().DecodeAsync(
            [0x00, 0x01, 0x02, 0x03], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnknownImageFormatException>();
    }
}
