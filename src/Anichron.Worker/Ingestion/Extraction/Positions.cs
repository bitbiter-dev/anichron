using MetadataExtractor.Formats.Exif;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

internal static partial class Positions
{
    public static (double? Latitude, double? Longitude) Of(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is null)
            return (null, null);
        try
        {
            // GetGeoLocation() returns Nullable<GeoLocation>; the pattern unwraps it.
            var location = gps.GetGeoLocation();
            return location is { } coordinates
                ? (coordinates.Latitude, coordinates.Longitude)
                : (null, null);
        }
        // Stryker disable once Block
        catch (Exception ex)
        {
            Log.GpsCoordinatesParseFailed(logger, ex);
            return (null, null);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse GPS coordinates from EXIF data.")]
        public static partial void GpsCoordinatesParseFailed(ILogger logger, Exception exception);
    }
}
