using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ImageCompletenessTests
{
    private const string HeifEndsEarly = "The image data is incomplete: the file ends inside a HEIF box.";
    private const string JpegEndsEarly = "The image data is incomplete: the file ends before the JPEG's end-of-image marker.";
    private const string WebpEndsEarly = "The image data is incomplete: the file ends before the size its RIFF header gives.";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

    // ==========================================================================
    // HEIF
    // ==========================================================================

    // ftyp (44 bytes) at 0, meta (4479 bytes) at 44, then mdat at 4523 with a 64-bit size that ends at the file's end.
    private static byte[] IphoneHeic() => EmbeddedFixtures.Read("iphone-4732.heic");

    private static Action CheckHeif(byte[] heif) => () => ImageCompleteness.EnsureHeif(heif);

    [Fact]
    public void EnsureHeif_WholeIphoneHeic_Passes()
        => CheckHeif(IphoneHeic()).Should().NotThrow();

    [Theory]
    [InlineData(4000)]
    [InlineData(4525)]
    [InlineData(4527)]
    [InlineData(4535)]
    [InlineData(1_000_000)]
    [InlineData(2_516_053)]
    public void EnsureHeif_IphoneHeicCutShort_Throws(int length)
        => CheckHeif(IphoneHeic()[..length]).Should().Throw<IncompleteImageException>().WithMessage(HeifEndsEarly);

    [Fact]
    public void EnsureHeif_LastBoxEndsExactlyAtTheEndWithAnEmptyBody_Passes()
        => CheckHeif(Hex("00000008 66726565")).Should().NotThrow();

    [Fact]
    public void EnsureHeif_LastBoxHasA64BitSizeAndAnEmptyBody_Passes()
        => CheckHeif(Hex("00000001 6D646174 0000000000000010")).Should().NotThrow();

    [Fact]
    public void EnsureHeif_LastBoxSizedZero_RunsToTheEndAndPasses()
        => CheckHeif(Hex("00000000 6D646174 0102030405")).Should().NotThrow();

    [Fact]
    public void EnsureHeif_BoxOneBytePastTheEnd_Throws()
        => CheckHeif(Hex("00000009 66726565")).Should().Throw<IncompleteImageException>();

    // ==========================================================================
    // JPEG
    // ==========================================================================

    private static byte[] BurstJpeg() => EmbeddedFixtures.Read("burst-4386.jpg");

    private static Action CheckJpeg(byte[] jpeg) => () => ImageCompleteness.EnsureJpeg(jpeg);

    [Fact]
    public void EnsureJpeg_WholeIphoneJpeg_Passes()
        => CheckJpeg(BurstJpeg()).Should().NotThrow();

    [Fact]
    public void EnsureJpeg_IphoneJpegCutToHalf_Throws()
    {
        var jpeg = BurstJpeg();

        CheckJpeg(jpeg[..(jpeg.Length / 2)]).Should().Throw<IncompleteImageException>().WithMessage(JpegEndsEarly);
    }

    [Fact]
    public void EnsureJpeg_IphoneJpegWithoutItsLastByte_Throws()
        => CheckJpeg(BurstJpeg()[..^1]).Should().Throw<IncompleteImageException>();

    [Fact]
    public void EnsureJpeg_BytesAfterTheEndOfImage_Pass()
        => CheckJpeg([.. BurstJpeg(), .. Hex("FFD8 FFE1 0010 4D504600")]).Should().NotThrow();

    // An APP1 segment that holds a whole thumbnail, its own end-of-image marker included.
    private static byte[] WithThumbnail(byte[] jpeg)
    {
        var thumbnail = EmbeddedFixtures.Read("exif-camera.jpg");
        var length = thumbnail.Length + 2;
        return [.. jpeg[..2], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. thumbnail, .. jpeg[2..]];
    }

    [Fact]
    public void EnsureJpeg_WholeJpegWithAThumbnail_Passes()
        => CheckJpeg(WithThumbnail(BurstJpeg())).Should().NotThrow();

    [Fact]
    public void EnsureJpeg_CutJpegWithAWholeThumbnail_Throws()
    {
        var jpeg = WithThumbnail(BurstJpeg());

        CheckJpeg(jpeg[..(jpeg.Length / 2)]).Should().Throw<IncompleteImageException>();
    }

    [Theory]
    [InlineData("FFDA 0003 00 1122 FF00 33 FFD9")]
    [InlineData("FFDA 0003 00 1122 FF01 33 FFD9")]
    [InlineData("FFDA 0003 00 1122 FFD0 33 FFD9")]
    [InlineData("FFDA 0003 00 1122 FFD7 33 FFD9")]
    [InlineData("FFDA 0003 00 1122 FFFFFF D9")]
    [InlineData("FFDA 0003 00 11 FFC4 0003 00 FFDA 0003 00 22 FFD9")]
    [InlineData("00 FFD9")]
    public void EnsureJpeg_MarkersWithinAndBetweenScans_ReachTheEndOfImage(string afterStartOfImage)
        => CheckJpeg(Hex("FFD8 " + afterStartOfImage)).Should().NotThrow();

    [Theory]
    [InlineData("")]
    [InlineData("0000")]
    [InlineData("00D9")]
    [InlineData("FFFF")]
    [InlineData("FFE0")]
    [InlineData("FFE0 00")]
    [InlineData("FFE0 0010 00")]
    [InlineData("FFE0 0002")]
    [InlineData("FFDA 0003 00 1122 FF00 33")]
    [InlineData("FFDA 0003 00 11 FFC4 0003 00 FFDA 0003 00 22")]
    public void EnsureJpeg_DataThatEndsBeforeTheEndOfImage_Throws(string afterStartOfImage)
        => CheckJpeg(Hex("FFD8 " + afterStartOfImage)).Should().Throw<IncompleteImageException>().WithMessage(JpegEndsEarly);

    // ==========================================================================
    // WebP
    // ==========================================================================

    // RIFF size 76, so the whole file is 84 bytes.
    private static byte[] ExifWebp() => EmbeddedFixtures.Read("exif-image.webp");

    private static Action CheckWebp(byte[] webp) => () => ImageCompleteness.EnsureWebp(webp);

    [Fact]
    public void EnsureWebp_WholeWebp_Passes()
        => CheckWebp(ExifWebp()).Should().NotThrow();

    [Theory]
    [InlineData(12)]
    [InlineData(42)]
    [InlineData(83)]
    public void EnsureWebp_WebpCutShort_Throws(int length)
        => CheckWebp(ExifWebp()[..length]).Should().Throw<IncompleteImageException>().WithMessage(WebpEndsEarly);

    [Fact]
    public void EnsureWebp_RiffSizeOneBytePastTheEnd_Throws()
        => CheckWebp([.. Hex("52494646 4D000000"), .. ExifWebp()[8..]])
            .Should().Throw<IncompleteImageException>().WithMessage(WebpEndsEarly);

    [Fact]
    public void EnsureWebp_BytesAfterTheRiffSize_Pass()
        => CheckWebp([.. ExifWebp(), .. Hex("00 52494646")]).Should().NotThrow();

    [Fact]
    public void EnsureWebp_RiffSizeAtTheTopOfItsRange_Throws()
        => CheckWebp([.. Hex("52494646 FFFFFFFF"), .. ExifWebp()[8..]]).Should().Throw<IncompleteImageException>();
}
