using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion;

internal sealed record ExifData(
    int Width,
    int Height,
    int Orientation,
    LocalDateTime? DateCaptured,
    double? Latitude,
    double? Longitude,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    int? DurationInSeconds)
{
    internal static ExifData Empty { get; } = new(0, 0, ExifOrientation.Upright, null, null, null, null, null, null, null);
}
