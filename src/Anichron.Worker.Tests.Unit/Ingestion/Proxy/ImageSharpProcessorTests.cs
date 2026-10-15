using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ImageSharpProcessorTests
{
    private static readonly WorkerSettings Settings = new()
    {
        ThumbnailShortEdge = 300,
        ThumbnailJpegQuality = 60,
        PreviewLongEdge = 800,
        PreviewJpegQuality = 90,
        BlurhashSampleLongEdge = 32,
    };

    private static ImageSharpProcessor Processor() => new(Options.Create(Settings));

    private static Image<Rgb24> Gradient(int width, int height)
    {
        var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgb24((byte)(x * 255 / width), (byte)(y * 255 / height), 128);
            }
        });
        return image;
    }

    private static Image Decode(byte[] jpeg) => Image.Load(jpeg);

    [Fact]
    public async Task EncodeThumbnailAsync_EncodesAtTheGivenSizeAsync()
    {
        using var source = Gradient(1200, 600);

        using var thumbnail = Decode(await Processor().EncodeThumbnailAsync(source, TestContext.Current.CancellationToken));

        thumbnail.Size.Should().Be(new Size(1200, 600));
    }

    [Fact]
    public async Task EncodeThumbnailAsync_EncodesWithTheConfiguredQualityAsync()
    {
        using var source = Gradient(1200, 600);

        using var thumbnail = Decode(await Processor().EncodeThumbnailAsync(source, TestContext.Current.CancellationToken));

        thumbnail.Metadata.GetJpegMetadata().Quality.Should().Be(60);
    }

    [Fact]
    public async Task EncodeFullPreviewAsync_EncodesAtTheGivenSizeAsync()
    {
        using var source = Gradient(1600, 1000);

        using var preview = Decode(await Processor().EncodeFullPreviewAsync(source, TestContext.Current.CancellationToken));

        preview.Size.Should().Be(new Size(1600, 1000));
    }

    [Fact]
    public async Task EncodeFullPreviewAsync_EncodesWithTheConfiguredQualityAsync()
    {
        using var source = Gradient(400, 300);

        using var preview = Decode(await Processor().EncodeFullPreviewAsync(source, TestContext.Current.CancellationToken));

        preview.Metadata.GetJpegMetadata().Quality.Should().Be(90);
    }

    [Fact]
    public async Task ComputeBlurhashAsync_LeavesTheImageSizeUnchangedAsync()
    {
        using var source = Gradient(640, 320);

        await Processor().ComputeBlurhashAsync(source, TestContext.Current.CancellationToken);

        source.Size.Should().Be(new Size(640, 320));
    }

    [Fact]
    public async Task ComputeBlurhashAsync_EncodesFourByThreeComponentsAsync()
    {
        using var source = Gradient(640, 320);

        var hash = await Processor().ComputeBlurhashAsync(source, TestContext.Current.CancellationToken);

        hash.Should().HaveLength(28);
        hash[0].Should().Be('L');
    }
}
