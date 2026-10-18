using Anichron.Worker.Ingestion;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class CaptureDateTests
{
    private static MockFileSystem FileWrittenAt(DateTime lastWrite) => new(new Dictionary<string, MockFileData>
    {
        ["/abs/photo.jpg"] = new MockFileData([]) { LastWriteTime = new DateTimeOffset(lastWrite) },
    });

    [Fact]
    public void Resolve_WithAnExifDate_ReturnsItWithoutReadingTheFile()
    {
        var exifDate = new LocalDateTime(2020, 1, 2, 3, 4, 5);

        CaptureDate.Resolve(exifDate, new MockFileSystem(), "/abs/missing.jpg").Should().Be(exifDate);
    }

    [Fact]
    public void Resolve_WithoutAnExifDate_FallsBackToTheFilesLastWriteTime()
    {
        var fileSystem = FileWrittenAt(new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Local));

        CaptureDate.Resolve(null, fileSystem, "/abs/photo.jpg").Should().Be(new LocalDateTime(2023, 6, 15, 12, 0));
    }
}
