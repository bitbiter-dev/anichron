using Anichron.Core.Domain;
using GeoTimeZone;
using NodaTime;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>
/// The time zones a capture date is read in: the zone at a position, looked up offline (GeoTimeZone's boundary data,
/// then NodaTime's tzdb), else the library's zone.
/// </summary>
/// <param name="zones">The zones the lookup's ids resolve in.</param>
/// <param name="library">The library's zone, the host's system zone.</param>
/// <param name="logger">Logs an id the zones do not know.</param>
internal sealed partial class CaptureZones(IDateTimeZoneProvider zones, DateTimeZone library, ILogger<CaptureZones> logger)
{
    /// <summary>The capture date of what extraction read, null when it is undated; an undated asset is never looked up.</summary>
    /// <param name="extracted">What extraction read from the file.</param>
    public CaptureDate? DateOf(ExtractedMedia extracted) => extracted.Recorded?.In(At(extracted.Description), library);

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
