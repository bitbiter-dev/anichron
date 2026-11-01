using Anichron.Worker.Bursts;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace Anichron.Worker.Tests.Unit.Bursts;

public sealed class OriginalPhotoReaderTests
{
    private readonly FakeLogger<OriginalPhotoReader> logger = new();

    // A JPEG whose EXIF holds only DateTimeOriginal: IFD0 points at a sub-IFD with that one tag.
    private static byte[] JpegCapturedAt(string exifDate)
    {
        var tiff = new List<byte>();
        tiff.AddRange([0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00]); // little endian, IFD0 at 8
        tiff.AddRange([0x01, 0x00, 0x69, 0x87, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 26, 0x00, 0x00, 0x00, 0, 0, 0, 0]); // ExifOffset = 26
        tiff.AddRange([0x01, 0x00, 0x03, 0x90, 0x02, 0x00, 20, 0x00, 0x00, 0x00, 44, 0x00, 0x00, 0x00, 0, 0, 0, 0]); // DateTimeOriginal at 44
        tiff.AddRange(Encoding.ASCII.GetBytes(exifDate + "\0"));
        var length = tiff.Count + 8;
        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. "Exif\0\0"u8, .. tiff, 0xFF, 0xD9];
    }

    private OriginalPhotoFacts Read(MockFileSystem fileSystem)
        => new OriginalPhotoReader(fileSystem, logger).Read("/data/originals/mira/a.jpg");

    [Fact]
    public void Read_PhotoWithoutABurstLabel_KnowsNoLabel()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/data/originals/mira/a.jpg"] = new(JpegCapturedAt("2024:07:15 13:22:01")),
        });

        Read(fileSystem).Should().Be(new OriginalPhotoFacts(CameraBurstId: null));
        logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public void Read_MissingOriginal_LogsAWarningAndKnowsNothing()
    {
        var facts = Read(new MockFileSystem());

        facts.Should().Be(OriginalPhotoFacts.Unknown);
        logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r =>
            r.Level == LogLevel.Warning
            && r.Message == "Could not read /data/originals/mira/a.jpg for burst detection; it is grouped by similarity alone."
            && r.Exception is FileNotFoundException);
    }

    [Fact]
    public void Describe_AppleBurst_ReadsTheBurstUuid()
    {
        var apple = new AppleMakernoteDirectory();
        apple.Set(AppleMakernoteDirectory.TagBurstUuid, "3F2504E0-4F89-11D3-9A0C-0305E82C3301");

        OriginalPhotoReader.Describe([new ExifSubIfdDirectory(), apple]).Should().Be(
            new OriginalPhotoFacts("3F2504E0-4F89-11D3-9A0C-0305E82C3301"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Describe_BlankBurstUuid_IsNoLabel(string blank)
    {
        var apple = new AppleMakernoteDirectory();
        apple.Set(AppleMakernoteDirectory.TagBurstUuid, blank);

        OriginalPhotoReader.Describe([apple]).CameraBurstId.Should().BeNull();
    }

    [Fact]
    public void Describe_NoMakerNote_KnowsNothing()
        => OriginalPhotoReader.Describe([new ExifIfd0Directory()]).Should().Be(OriginalPhotoFacts.Unknown);
}
