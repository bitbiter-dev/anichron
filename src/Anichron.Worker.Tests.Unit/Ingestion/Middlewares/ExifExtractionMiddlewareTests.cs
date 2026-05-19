using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
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

    private static IngestionContext MakeContext(string path = "/abs/photo.jpg", string? contentHash = "deadbeef") => new()
    {
        Item = new SingleFileItem(path, "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        ContentHash = contentHash,
    };

    private static ExifExtractionMiddleware MakeMiddleware(MockFileSystem fs)
        => new(fs, Substitute.For<ILogger<ExifExtractionMiddleware>>());

    // ==========================================================================
    // CanInvoke
    // ==========================================================================

    [Fact]
    public void CanInvoke_WhenHashSet_ReturnsTrue()
    {
        var middleware = MakeMiddleware(new MockFileSystem());
        var context = MakeContext();
        context.ContentHash = "abc123";
        middleware.CanInvoke(context).Should().BeTrue();
    }

    [Fact]
    public void CanInvoke_WhenHashNull_ReturnsFalse()
    {
        var middleware = MakeMiddleware(new MockFileSystem());
        middleware.CanInvoke(MakeContext(contentHash: null)).Should().BeFalse();
    }

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

        context.Exif.Should().NotBeNull();
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

        context.Exif!.Width.Should().Be(100);
        context.Exif!.Height.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ExifOrientationSix_MapsToNinetyDegreesAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData(JpegWithExif),
        });
        var context = MakeContext("/abs/photo.jpg");

        await MakeMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Exif!.OrientationDegrees.Should().Be(90);
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

        context.Exif!.Width.Should().Be(0);
        context.Exif!.Height.Should().Be(0);
        context.Exif!.OrientationDegrees.Should().Be(0);
        context.Exif!.DateCaptured.Should().BeNull();
        context.Exif!.Latitude.Should().BeNull();
        context.Exif!.Longitude.Should().BeNull();
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

        context.Exif!.Width.Should().Be(0);
        context.Exif!.Height.Should().Be(0);
        context.Exif!.OrientationDegrees.Should().Be(0);
        context.Exif!.DateCaptured.Should().BeNull();
        context.Exif!.Latitude.Should().BeNull();
        context.Exif!.Longitude.Should().BeNull();
    }
}
