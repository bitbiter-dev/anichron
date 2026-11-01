using MetadataExtractor;
using MetadataExtractor.Formats.Exif.Makernotes;
using System.IO.Abstractions;

namespace Anichron.Worker.Bursts;

/// <summary>What burst detection reads from a photo's original.</summary>
/// <param name="CameraBurstId">Apple's BurstUUID, or null when the camera wrote none.</param>
internal sealed record OriginalPhotoFacts(string? CameraBurstId)
{
    internal static OriginalPhotoFacts Unknown { get; } = new(CameraBurstId: null);
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
        var burstId = directories.OfType<AppleMakernoteDirectory>().FirstOrDefault()?.GetString(AppleMakernoteDirectory.TagBurstUuid);
        return new(string.IsNullOrWhiteSpace(burstId) ? null : burstId);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path} for burst detection; it is grouped by similarity alone.")]
        public static partial void Unreadable(ILogger logger, string path, Exception exception);
    }
}
