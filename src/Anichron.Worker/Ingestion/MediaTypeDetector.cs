using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion;

internal static class MediaTypeDetector
{
    public static MediaType? Detect(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".heic" or ".heif"
                or ".gif" or ".webp" or ".tiff" or ".tif" or ".bmp" => MediaType.Image,
            ".mov" or ".mp4" or ".m4v" or ".avi" or ".mkv" or ".wmv" => MediaType.Video,
            _ => null,
        };
}
