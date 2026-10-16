using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ImageProxyGeneratorsTests
{
    private static readonly IOptions<WorkerSettings> DefaultOptions = Options.Create(new WorkerSettings
    {
        ThumbnailShortEdge = 300,
        PreviewLongEdge = 1920,
        BlurhashSampleLongEdge = 64,
    });

    [Fact]
    public void ThumbnailGenerator_Bound_IsShortEdgeFromSettings()
    {
        new ThumbnailGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).Bound
            .Should().Be(new ProxyBound(ProxyEdge.ShortEdge, 300));
    }

    [Fact]
    public void FullPreviewGenerator_Bound_IsLongEdgeFromSettings()
    {
        new FullPreviewGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).Bound
            .Should().Be(new ProxyBound(ProxyEdge.LongEdge, 1920));
    }

    [Fact]
    public void BlurhashGenerator_Bound_IsLongEdgeFromSettings()
    {
        new BlurhashGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).Bound
            .Should().Be(new ProxyBound(ProxyEdge.LongEdge, 64));
    }

    // ==========================================================================
    // ThumbnailGenerator
    // ==========================================================================

    [Fact]
    public void ThumbnailGenerator_FileName_IsThumbnailJpg()
    {
        new ThumbnailGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).FileName.Should().Be("thumbnail.jpg");
    }

    [Fact]
    public void ThumbnailGenerator_ProxyType_IsThumbnail()
    {
        new ThumbnailGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).ProxyType.Should().Be(ProxyType.Thumbnail);
    }

    [Fact]
    public async Task ThumbnailGenerator_GenerateAsync_DelegatesToEncodeThumbnailAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.EncodeThumbnailAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
            .Returns([0x01, 0x02]);
        using var image = new Image<Rgb24>(1, 1);

        var bytes = await new ThumbnailGenerator(processor, DefaultOptions).GenerateAsync(image, CancellationToken.None);

        bytes.Should().Equal(0x01, 0x02);
    }

    // ==========================================================================
    // FullPreviewGenerator
    // ==========================================================================

    [Fact]
    public void FullPreviewGenerator_FileName_IsPreviewJpg()
    {
        new FullPreviewGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).FileName.Should().Be("preview.jpg");
    }

    [Fact]
    public void FullPreviewGenerator_ProxyType_IsFullPreview()
    {
        new FullPreviewGenerator(Substitute.For<IImageProcessor>(), DefaultOptions).ProxyType.Should().Be(ProxyType.FullPreview);
    }

    [Fact]
    public async Task FullPreviewGenerator_GenerateAsync_DelegatesToEncodeFullPreviewAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.EncodeFullPreviewAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
            .Returns([0x03, 0x04]);
        using var image = new Image<Rgb24>(1, 1);

        var bytes = await new FullPreviewGenerator(processor, DefaultOptions).GenerateAsync(image, CancellationToken.None);

        bytes.Should().Equal(0x03, 0x04);
    }

    // ==========================================================================
    // BlurhashGenerator
    // ==========================================================================

    [Fact]
    public async Task GenerateAsync_ReturnsUtf8EncodedHashBytesAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.ComputeBlurhashAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
            .Returns("HASH");
        using var image = new Image<Rgb24>(1, 1);

        var bytes = await new BlurhashGenerator(processor, DefaultOptions).GenerateAsync(image, CancellationToken.None);

        bytes.Should().Equal("HASH"u8.ToArray());
    }
}
