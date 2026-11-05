namespace Anichron.Core.Domain;

/// <summary>What extraction reads from a media file, apart from its capture date.</summary>
/// <param name="Width">The stored width in pixels.</param>
/// <param name="Height">The stored height in pixels.</param>
/// <param name="Orientation">The EXIF orientation value, 1-8.</param>
/// <param name="Latitude">The position's latitude.</param>
/// <param name="Longitude">The position's longitude.</param>
/// <param name="CameraMake">The camera's maker.</param>
/// <param name="CameraModel">The camera's model.</param>
/// <param name="LensModel">The lens's model.</param>
/// <param name="DurationInSeconds">A video's duration.</param>
public sealed record MediaDescription(
    int Width,
    int Height,
    int Orientation,
    double? Latitude,
    double? Longitude,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    int? DurationInSeconds)
{
    /// <summary>A file nothing could be read from. A new value each time, since a row owns its own.</summary>
    public static MediaDescription Unknown => new(0, 0, ExifOrientation.Upright, null, null, null, null, null, null);
}
