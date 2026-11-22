using Anichron.Worker.Ingestion.Proxy;
using SixLabors.ImageSharp;

namespace Anichron.Worker.Ingestion;

internal static class BrokenFile
{
    internal static bool Rejected(Exception exception)
        => exception is IncompleteImageException
            or ImageFormatException
            or MetadataExtractor.ImageProcessingException
            or FfmpegException;
}
