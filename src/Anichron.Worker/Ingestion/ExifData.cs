using NodaTime;

namespace Anichron.Worker.Ingestion;

internal sealed record ExifData(
    int Width,
    int Height,
    int OrientationDegrees,
    LocalDateTime? DateCaptured,
    float? Latitude,
    float? Longitude,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    float? DurationSeconds);
