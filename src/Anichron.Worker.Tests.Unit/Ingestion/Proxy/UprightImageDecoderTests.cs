using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class UprightImageDecoderTests
{
    private static Task DecodeAsync(byte[] original)
        => new UprightImageDecoder(new MagickHeifDecoder(), new ImageSharpUprightDecoder())
            .DecodeAsync(original, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("iphone-4732.heic")]
    [InlineData("burst-4386.jpg")]
    [InlineData("burst-4387.jpg")]
    [InlineData("exif-image.png")]
    [InlineData("exif-image.tiff")]
    [InlineData("exif-image.bmp")]
    [InlineData("exif-image.gif")]
    [InlineData("exif-image.webp")]
    public async Task DecodeAsync_CompleteSample_DecodesAsync(string fixture)
    {
        var act = () => DecodeAsync(EmbeddedFixtures.Read(fixture));

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("iphone-4732.heic")]
    [InlineData("burst-4386.jpg")]
    public async Task DecodeAsync_SampleCutToHalf_ThrowsThatItsDataIsIncompleteAsync(string fixture)
    {
        var original = EmbeddedFixtures.Read(fixture);

        var act = () => DecodeAsync(original[..(original.Length / 2)]);

        await act.Should().ThrowAsync<IncompleteImageException>();
    }
}
