using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Classification;

internal static class MetadataValues
{
    /// <summary>Copies every value of <paramref name="source"/> onto <paramref name="target"/>, keeping the target's key.</summary>
    /// <param name="source">The values to copy.</param>
    /// <param name="target">The row that takes them.</param>
    public static void Copy(Metadata source, Metadata target)
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.Orientation = source.Orientation;
        target.Latitude = source.Latitude;
        target.Longitude = source.Longitude;
        target.CameraMake = source.CameraMake;
        target.CameraModel = source.CameraModel;
        target.LensModel = source.LensModel;
        target.DurationInSeconds = source.DurationInSeconds;
        target.Blurhash = source.Blurhash;
    }
}
