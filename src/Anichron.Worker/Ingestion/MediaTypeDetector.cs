using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion;

internal static class MediaTypeDetector
{
    private static readonly Dictionary<string, MediaType> extensionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = MediaType.Image,
            [".jpeg"] = MediaType.Image,
            [".png"] = MediaType.Image,
            [".heic"] = MediaType.Image,
            [".heif"] = MediaType.Image,
            [".gif"] = MediaType.Image,
            [".webp"] = MediaType.Image,
            [".tiff"] = MediaType.Image,
            [".tif"] = MediaType.Image,
            [".bmp"] = MediaType.Image,
            [".mov"] = MediaType.Video,
            [".mp4"] = MediaType.Video,
            [".m4v"] = MediaType.Video,
            [".avi"] = MediaType.Video,
            [".mkv"] = MediaType.Video,
            [".wmv"] = MediaType.Video,
        };

    public static MediaType? Detect(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extensionMap.TryGetValue(extension, out var type) ? type : null;
    }
}
