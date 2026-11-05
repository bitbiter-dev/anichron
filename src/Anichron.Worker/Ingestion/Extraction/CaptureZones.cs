using Anichron.Core.Domain;
using GeoTimeZone;
using NodaTime;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>The time zone at a position, looked up offline (GeoTimeZone's boundary data, then NodaTime's tzdb).</summary>
/// <param name="zones">The zones the lookup's ids resolve in.</param>
/// <param name="logger">Logs an id the zones do not know.</param>
internal sealed partial class CaptureZones(IDateTimeZoneProvider zones, ILogger<CaptureZones> logger)
{
    /// <summary>
    /// The zone at the described position; null without a position on the globe, or when NodaTime does not know the
    /// zone found. The lookup answers even for coordinates off the globe, so those count as no position.
    /// </summary>
    /// <param name="description">The asset's description, holding its position.</param>
    public DateTimeZone? At(MediaDescription description)
    {
        if (description is not { Latitude: >= -90 and <= 90 and var latitude, Longitude: >= -180 and <= 180 and var longitude })
            return null;

        var id = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
        var zone = zones.GetZoneOrNull(id);
        if (zone is null)
            Log.UnknownZone(logger, id, latitude, longitude);
        return zone;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "No time zone for {Latitude}, {Longitude}: NodaTime does not know '{ZoneId}'.")]
        public static partial void UnknownZone(ILogger logger, string zoneId, double latitude, double longitude);
    }
}
