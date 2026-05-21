using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ImageProxyGeneratorsTests
{
    // ==========================================================================
    // TwoLevelHexShardStrategy
    // ==========================================================================

    [Fact]
    public void GetDirectory_ProducesCorrectTwoLevelPath()
    {
        var id = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");

        var result = new TwoLevelHexShardStrategy().GetDirectory(id);

        result.Should().Be("ab/cdef1234567890abcdef1234567890");
    }

    [Fact]
    public void GetDirectory_TopDirectoryIsAlwaysTwoChars()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var top = new TwoLevelHexShardStrategy().GetDirectory(id).Split('/')[0];

        top.Should().HaveLength(2);
    }

    // ==========================================================================
    // ThumbnailGenerator
    // ==========================================================================

    [Fact]
    public void ThumbnailGenerator_FileName_IsThumbnailJpg()
    {
        new ThumbnailGenerator(Substitute.For<IImageProcessor>()).FileName.Should().Be("thumbnail.jpg");
    }

    [Fact]
    public void ThumbnailGenerator_ProxyType_IsThumbnail()
    {
        new ThumbnailGenerator(Substitute.For<IImageProcessor>()).ProxyType.Should().Be(ProxyType.Thumbnail);
    }

    [Fact]
    public async Task ThumbnailGenerator_GenerateAsync_DelegatesToCreateThumbnailAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.CreateThumbnailAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns([0x01, 0x02]);

        var bytes = await new ThumbnailGenerator(processor).GenerateAsync(Stream.Null, CancellationToken.None);

        bytes.Should().Equal(0x01, 0x02);
    }

    // ==========================================================================
    // FullPreviewGenerator
    // ==========================================================================

    [Fact]
    public void FullPreviewGenerator_FileName_IsPreviewJpg()
    {
        new FullPreviewGenerator(Substitute.For<IImageProcessor>()).FileName.Should().Be("preview.jpg");
    }

    [Fact]
    public void FullPreviewGenerator_ProxyType_IsFullPreview()
    {
        new FullPreviewGenerator(Substitute.For<IImageProcessor>()).ProxyType.Should().Be(ProxyType.FullPreview);
    }

    [Fact]
    public async Task FullPreviewGenerator_GenerateAsync_DelegatesToCreateFullPreviewAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.CreateFullPreviewAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns([0x03, 0x04]);

        var bytes = await new FullPreviewGenerator(processor).GenerateAsync(Stream.Null, CancellationToken.None);

        bytes.Should().Equal(0x03, 0x04);
    }

    // ==========================================================================
    // BlurhashGenerator
    // ==========================================================================

    [Fact]
    public async Task GenerateAsync_ReturnsUtf8EncodedHashBytesAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.ComputeBlurhashAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns("HASH");

        var bytes = await new BlurhashGenerator(processor).GenerateAsync(Stream.Null, CancellationToken.None);

        bytes.Should().Equal("HASH"u8.ToArray());
    }
}
