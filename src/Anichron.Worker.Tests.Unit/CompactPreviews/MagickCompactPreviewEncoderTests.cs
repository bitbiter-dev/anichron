using Anichron.Worker.CompactPreviews;
using Anichron.Worker.Settings;
using ImageMagick;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Tests.Unit.CompactPreviews;

public sealed class MagickCompactPreviewEncoderTests
{
    private static byte[] Jpeg(int width, int height)
    {
        var random = new Random(592);
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                foreach (ref var pixel in rows.GetRowSpan(y))
                    pixel = new Rgb24((byte)random.Next(256), (byte)(y % 256), (byte)random.Next(256));
            }
        });
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] Encode(byte[] preview, WorkerSettings? settings = null)
        => new MagickCompactPreviewEncoder(Options.Create(settings ?? new WorkerSettings())).Encode(preview);

    private static (MagickFormat Format, uint Width, uint Height) Decoded(byte[] encoded)
    {
        using var image = new MagickImage(encoded);
        return (image.Format, image.Width, image.Height);
    }

    [Fact]
    public void TheBundledNativeLibrary_ReadsAndWritesJpegXl()
        => MagickNET.SupportedFormats.Should().ContainSingle(f => f.Format == MagickFormat.Jxl)
            .Which.Should().Match<IMagickFormatInfo>(f => f.SupportsReading && f.SupportsWriting);

    [Theory]
    [InlineData(2000, 1000, 1280, 640)]
    [InlineData(1000, 2000, 640, 1280)]
    [InlineData(1280, 640, 1280, 640)]
    [InlineData(1000, 500, 1000, 500)]
    public void Encode_FitsTheLongEdgeWithoutEnlargingAsJpegXl(int width, int height, uint expectedWidth, uint expectedHeight)
        => Decoded(Encode(Jpeg(width, height))).Should().Be((MagickFormat.Jxl, expectedWidth, expectedHeight));

    [Fact]
    public void Encode_FollowsTheConfiguredLongEdge()
        => Decoded(Encode(Jpeg(400, 200), new WorkerSettings { CompactPreviewLongEdge = 100 }))
            .Should().Be((MagickFormat.Jxl, 100u, 50u));

    [Fact]
    public void Encode_FollowsTheConfiguredQuality()
    {
        var preview = Jpeg(300, 200);

        Encode(preview, new WorkerSettings { CompactPreviewQuality = 30 }).Length
            .Should().BeLessThan(Encode(preview, new WorkerSettings { CompactPreviewQuality = 85 }).Length);
    }

    [Fact]
    public void Encode_FollowsTheConfiguredEffort()
    {
        var preview = Jpeg(300, 200);

        Encode(preview, new WorkerSettings { CompactPreviewEffort = 3 }).Length
            .Should().NotBe(Encode(preview, new WorkerSettings { CompactPreviewEffort = 9 }).Length);
    }
}
