using Anichron.Worker.Ingestion;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Bursts;

/// <summary>What burst detection reads from a photo's original.</summary>
/// <param name="ExifCapturedAt">The EXIF capture time, or null when the original has none.</param>
/// <param name="CameraBurstId">Apple's BurstUUID, or null when the camera wrote none.</param>
internal sealed record OriginalPhotoFacts(LocalDateTime? ExifCapturedAt, string? CameraBurstId)
{
    internal static OriginalPhotoFacts Unknown { get; } = new(null, null);
}

internal interface IOriginalPhotoReader
{
    /// <summary>Reads the original once; an unreadable one is logged and yields no facts.</summary>
    /// <param name="absolutePath">The original's path in the container.</param>
    OriginalPhotoFacts Read(string absolutePath);
}

internal sealed partial class OriginalPhotoReader(IFileSystem fileSystem, ILogger<OriginalPhotoReader> logger)
    : IOriginalPhotoReader
{
    public OriginalPhotoFacts Read(string absolutePath)
    {
        try
        {
            using var stream = fileSystem.File.OpenRead(absolutePath);
            return Describe(ImageMetadataReader.ReadMetadata(stream));
        }
        catch (Exception ex)
        {
            Log.Unreadable(logger, absolutePath, ex);
            return OriginalPhotoFacts.Unknown;
        }
    }

    internal static OriginalPhotoFacts Describe(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var captured = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        var burstId = directories.OfType<AppleMakernoteDirectory>().FirstOrDefault()?.GetString(AppleMakernoteDirectory.TagBurstUuid);
        return new(
            ExifDate.Parse(captured),
            string.IsNullOrWhiteSpace(burstId) ? null : burstId);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path} for burst detection; it will not be grouped.")]
        public static partial void Unreadable(ILogger logger, string path, Exception exception);
    }
}
