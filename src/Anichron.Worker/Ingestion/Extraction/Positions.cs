using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using System.Globalization;
using System.Text.RegularExpressions;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

internal static partial class Positions
{
    public static (double? Latitude, double? Longitude) Of(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is null)
            return OfVideoLocation(directories, logger);
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

    private static (double? Latitude, double? Longitude) OfVideoLocation(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var raw = directories.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault()
            ?.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation);
        if (raw is null)
            return (null, null);
        if (Iso6709().Match(raw) is not { Success: true } match)
        {
            Log.Iso6709ParseFailed(logger, raw);
            return (null, null);
        }

        return (Degrees(match.Groups["latitude"].Value), Degrees(match.Groups["longitude"].Value));
    }

    private static double Degrees(string value) => double.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<latitude>[+-]\d{2}(\.\d+)?)(?<longitude>[+-]\d{3}(\.\d+)?)([+-]\d+(\.\d+)?)?/$", RegexOptions.CultureInvariant)]
    private static partial Regex Iso6709();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse GPS coordinates from EXIF data.")]
        public static partial void GpsCoordinatesParseFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No position: malformed ISO 6709 location '{Raw}'.")]
        public static partial void Iso6709ParseFailed(ILogger logger, string raw);
    }
}
