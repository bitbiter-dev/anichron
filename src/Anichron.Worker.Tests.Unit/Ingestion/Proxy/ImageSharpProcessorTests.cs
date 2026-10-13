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
        ThumbnailMaxWidth = 300,
        ThumbnailJpegQuality = 60,
        PreviewMaxWidth = 800,
        PreviewJpegQuality = 90,
        BlurhashSampleWidth = 32,
    };

    private static ImageSharpProcessor Processor() => new(Options.Create(Settings));

    private static Image<Rgba32> Gradient(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)(x * 255 / width), (byte)(y * 255 / height), 128);
            }
        });
        return image;
    }

    private static Image Decode(byte[] jpeg) => Image.Load(jpeg);

    [Fact]
    public async Task CreateThumbnailAsync_WideImage_FitsMaxWidthKeepingAspectRatioAsync()
    {
        using var source = Gradient(1200, 600);

        using var thumbnail = Decode(await Processor().CreateThumbnailAsync(source, TestContext.Current.CancellationToken));

        thumbnail.Width.Should().Be(300);
        thumbnail.Height.Should().Be(150);
    }

    [Fact]
    public async Task CreateThumbnailAsync_NarrowerThanMax_KeepsOriginalSizeAsync()
    {
        using var source = Gradient(100, 50);

        using var thumbnail = Decode(await Processor().CreateThumbnailAsync(source, TestContext.Current.CancellationToken));

        thumbnail.Width.Should().Be(100);
        thumbnail.Height.Should().Be(50);
    }

    [Fact]
    public async Task CreateThumbnailAsync_EncodesWithTheConfiguredQualityAsync()
    {
        using var source = Gradient(1200, 600);

        using var thumbnail = Decode(await Processor().CreateThumbnailAsync(source, TestContext.Current.CancellationToken));

        thumbnail.Metadata.GetJpegMetadata().Quality.Should().Be(60);
    }

    [Fact]
    public async Task CreateFullPreviewAsync_WiderThanMax_DownscalesToMaxWidthAsync()
    {
        using var source = Gradient(1600, 1000);

        using var preview = Decode(await Processor().CreateFullPreviewAsync(source, TestContext.Current.CancellationToken));

        preview.Width.Should().Be(800);
        preview.Height.Should().Be(500);
    }

    [Fact]
    public async Task CreateFullPreviewAsync_NarrowerThanMax_KeepsOriginalSizeAsync()
    {
        using var source = Gradient(400, 300);

        using var preview = Decode(await Processor().CreateFullPreviewAsync(source, TestContext.Current.CancellationToken));

        preview.Width.Should().Be(400);
        preview.Height.Should().Be(300);
    }

    [Fact]
    public async Task CreateFullPreviewAsync_ExactlyMaxWidth_LeavesThePixelsUntouchedAsync()
    {
        using var source = Gradient(800, 400);
        using var original = source.Clone();

        await Processor().CreateFullPreviewAsync(source, TestContext.Current.CancellationToken);

        source.Size.Should().Be(original.Size);
        for (var y = 0; y < source.Height; y += 7)
        {
            for (var x = 0; x < source.Width; x += 7)
                source[x, y].Should().Be(original[x, y]);
        }
    }

    [Fact]
    public async Task CreateFullPreviewAsync_EncodesWithTheConfiguredQualityAsync()
    {
        using var source = Gradient(400, 300);

        using var preview = Decode(await Processor().CreateFullPreviewAsync(source, TestContext.Current.CancellationToken));

        preview.Metadata.GetJpegMetadata().Quality.Should().Be(90);
    }

    [Fact]
    public async Task ComputeBlurhashAsync_SamplesDownToTheConfiguredWidthAsync()
    {
        using var source = Gradient(640, 320);

        await Processor().ComputeBlurhashAsync(source, TestContext.Current.CancellationToken);

        source.Width.Should().Be(32);
        source.Height.Should().Be(16);
    }

    [Fact]
    public async Task ComputeBlurhashAsync_NarrowerThanSampleWidth_IsScaledUpToItAsync()
    {
        using var source = Gradient(16, 8);

        await Processor().ComputeBlurhashAsync(source, TestContext.Current.CancellationToken);

        source.Width.Should().Be(32);
        source.Height.Should().Be(16);
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
