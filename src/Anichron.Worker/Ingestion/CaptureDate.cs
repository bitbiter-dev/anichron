using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion;

internal static class CaptureDate
{
    public static LocalDateTime Resolve(LocalDateTime? exifDate, IFileSystem fileSystem, string absolutePath)
        => exifDate ?? LocalDateTime.FromDateTime(fileSystem.FileInfo.New(absolutePath).LastWriteTime);
}
