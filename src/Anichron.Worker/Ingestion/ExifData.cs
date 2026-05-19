using NodaTime;

namespace Anichron.Worker.Ingestion;

internal sealed record ExifData(
    int Width,
    int Height,
    int OrientationDegrees,
    LocalDateTime? DateCaptured,
    double? Latitude,
    double? Longitude,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    int? DurationInSeconds)
{
    internal static ExifData Empty { get; } = new(0, 0, 0, null, null, null, null, null, null, null);
}
