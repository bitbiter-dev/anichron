using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Extraction;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using NodaTime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ExifExtractionMiddlewareTests
{
    // Minimal JPEG with EXIF IFD0: Width=100, Height=200, Orientation=6 (90°)
    private static readonly byte[] JpegWithExif =
    [
        0xFF, 0xD8,                                                               // SOI
        0xFF, 0xE1, 0x00, 0x3A,                                                  // APP1 marker + length (58)
        0x45, 0x78, 0x69, 0x66, 0x00, 0x00,                                     // "Exif\0\0"
        0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,                        // TIFF LE header; IFD0 at offset 8
        0x03, 0x00,                                                               // 3 IFD entries
        0x00, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, // Width = 100
        0x01, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x00, 0x00, // Height = 200
        0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, // Orientation = 6
        0x00, 0x00, 0x00, 0x00,                                                  // Next IFD = 0
        0xFF, 0xD9,                                                               // EOI
    ];

    private static readonly byte[] JpegWithoutExif = [0xFF, 0xD8, 0xFF, 0xD9];

    private static byte[] JpegWithOrientation(byte orientation)
    {
        var bytes = JpegWithExif.ToArray();
        bytes[Array.IndexOf(bytes, (byte)0x12) + 8] = orientation;
        return bytes;
    }

    private static IngestionContext MakeContext(
        string path = "/abs/photo.jpg", string? contentHash = "deadbeef", MediaType mediaType = MediaType.Image) => new()
        {
            Item = new SingleFileItem(path, "photo.jpg", mediaType),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AttemptId = Guid.NewGuid(),
            ContentHash = contentHash,
        };

    private static ExifExtractionMiddleware MakeMiddleware(MockFileSystem fs)
        => new(fs, Substitute.For<ILogger<ExifExtractionMiddleware>>());

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Always_CallsNextAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData(JpegWithExif),
        });
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await MakeMiddleware(fs).InvokeAsync(MakeContext("/abs/photo.jpg"), NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Always_SetsExifOnContextAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData(JpegWithExif),
        });
        var context = MakeContext("/abs/photo.jpg");

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Extracted.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ValidJpegExif_ExtractsCorrectDimensionsAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData(JpegWithExif),
        });
        var context = MakeContext("/abs/photo.jpg");

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Extracted!.Description.Width.Should().Be(100);
        context.Extracted.Description.Height.Should().Be(200);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 7)]
    [InlineData(8, 8)]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    public async Task InvokeAsync_ExifOrientation_StoresRawTagOrDefaultsToUprightAsync(byte tag, int expected)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData(JpegWithOrientation(tag)),
        });
        var context = MakeContext("/abs/photo.jpg");

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Extracted!.Description.Orientation.Should().Be(expected);
    }

    [Fact]
    public async Task InvokeAsync_MissingExif_SetsSafeDefaultsAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData(JpegWithoutExif),
        });
        var context = MakeContext("/abs/photo.jpg");

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Extracted.Should().Be(new ExtractedMedia(null, new MediaDescription(0, 0, 1, null, null, null, null, null, null)));
    }

    [Fact]
    public async Task InvokeAsync_UnreadableFile_SetsEmptyExifAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData([0x00, 0x00, 0x00, 0x00]),
        });
        var context = MakeContext("/abs/photo.jpg");

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Extracted.Should().Be(new ExtractedMedia(null, new MediaDescription(0, 0, 1, null, null, null, null, null, null)));
    }

    // ==========================================================================
    // Real files: size, orientation and duration
    // ==========================================================================

    private static byte[] Fixture(string name)
    {
        using var stream = typeof(ExifExtractionMiddlewareTests).Assembly.GetManifestResourceStream($"Fixtures.{name}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // The maintainer's stripped iPhone photo: stored 4032x3024, irot 270, EXIF orientation 6.
    private const int HeicIrotAngleOffset = 2401;
    private const int HeicExifOrientationOffset = 54180;

    private static byte[] HeicPatched(int offset, byte value)
    {
        var heic = Fixture("iphone-4732.heic");
        heic[offset] = value;
        return heic;
    }

    private static async Task<ExtractedMedia> ExtractFromAsync(byte[] file, MediaType mediaType)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/media"] = new MockFileData(file),
        });
        var context = MakeContext("/abs/media", mediaType: mediaType);

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        return context.Extracted!;
    }

    private static async Task<MediaDescription> ExtractAsync(byte[] file) => (await ExtractFromAsync(file, MediaType.Image)).Description;

    [Theory]
    [InlineData("exif-camera.jpg", 48, 32)]
    [InlineData("exif-image.png", 40, 24)]
    [InlineData("exif-image.tiff", 56, 36)]
    [InlineData("exif-image.gif", 44, 28)]
    [InlineData("exif-image.webp", 52, 30)]
    [InlineData("exif-image.bmp", 36, 20)]
    [InlineData("iphone-4732.heic", 4032, 3024)]
    public async Task InvokeAsync_Image_StoresItsPixelSizeAsync(string name, int width, int height)
    {
        var exif = await ExtractAsync(Fixture(name));

        (exif.Width, exif.Height).Should().Be((width, height));
    }

    [Fact]
    public async Task InvokeAsync_HeicWithIrot270_StoresOrientation6Async()
        => (await ExtractAsync(Fixture("iphone-4732.heic"))).Orientation.Should().Be(6);

    [Fact]
    public async Task InvokeAsync_HeicWhoseExifDisagreesWithIrot_StoresTheOrientationFromIrotAsync()
        => (await ExtractAsync(HeicPatched(HeicExifOrientationOffset, 1))).Orientation.Should().Be(6);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 8)]
    [InlineData(2, 3)]
    public async Task InvokeAsync_HeicIrot_MapsToTheEquivalentExifOrientationAsync(byte quarterTurns, int expected)
        => (await ExtractAsync(HeicPatched(HeicIrotAngleOffset, quarterTurns))).Orientation.Should().Be(expected);

    // Both MOVs carry an audio track (0x0) before the 1920x1080 video track; the portrait one is
    // rotated -90 as an iPhone records it.
    [Theory]
    [InlineData("exif-portrait.mov", 6)]
    [InlineData("exif-landscape.mov", 1)]
    public async Task InvokeAsync_Mov_StoresTheVideoTrackSizeWithItsRotationAsOrientationAsync(string name, int orientation)
    {
        var exif = await ExtractAsync(Fixture(name));

        (exif.Width, exif.Height, exif.Orientation).Should().Be((1920, 1080, orientation));
    }

    [Theory]
    [InlineData("exif-portrait.mov")]
    [InlineData("exif-clip.mp4")]
    public async Task InvokeAsync_Video_StoresItsDurationInWholeSecondsAsync(string name)
        => (await ExtractAsync(Fixture(name))).DurationInSeconds.Should().Be(4);

    [Fact]
    public async Task InvokeAsync_Mp4_StoresTheVideoTrackSizeAsync()
    {
        var exif = await ExtractAsync(Fixture("exif-clip.mp4"));

        (exif.Width, exif.Height).Should().Be((64, 48));
    }

    // ==========================================================================
    // Real files: the recorded date and position
    // ==========================================================================

    private static byte[] JpegTakenInAuckland()
    {
        static Rational[] Dms(uint degrees, uint minutes, uint seconds) => [new(degrees, 1), new(minutes, 1), new(seconds, 1)];
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.DateTimeOriginal, "2026:10:30 19:09:51");
        profile.SetValue(ExifTag.OffsetTimeOriginal, "+13:00");
        profile.SetValue(ExifTag.GPSLatitudeRef, "S");
        profile.SetValue(ExifTag.GPSLatitude, Dms(36, 51, 0));
        profile.SetValue(ExifTag.GPSLongitudeRef, "E");
        profile.SetValue(ExifTag.GPSLongitude, Dms(174, 45, 36));
        using var image = new Image<Rgb24>(8, 6);
        image.Metadata.ExifProfile = profile;
        using var jpeg = new MemoryStream();
        image.SaveAsJpeg(jpeg);
        return jpeg.ToArray();
    }

    [Fact]
    public async Task InvokeAsync_PhotoWithOffsetAndPosition_RecordsBothAsync()
    {
        var extracted = await ExtractFromAsync(JpegTakenInAuckland(), MediaType.Image);

        extracted.Recorded.Should().Be(new RecordedDate(new LocalDateTime(2026, 10, 30, 19, 9, 51), Offset.FromHours(13)));
        extracted.Description.Latitude.Should().BeApproximately(-36.85, 1e-9);
        extracted.Description.Longitude.Should().BeApproximately(174.76, 1e-9);
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_IsUndatedWhateverItsExifSaysAsync()
        => (await ExtractFromAsync(JpegTakenInAuckland(), MediaType.Video)).Recorded.Should().BeNull();
}
