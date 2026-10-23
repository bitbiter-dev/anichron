using NodaTime;
using NodaTime.Text;

namespace Anichron.Worker.Ingestion;

internal static class ExifDate
{
    // EXIF dates use colons as date separators ("2024:07:15 13:22:01"), unlike ISO 8601.
    private static LocalDateTimePattern Pattern
        => LocalDateTimePattern.CreateWithInvariantCulture("yyyy:MM:dd HH:mm:ss");

    /// <summary>The date, or null when the raw value is missing or not an EXIF date.</summary>
    /// <param name="raw">An EXIF date-time tag's value.</param>
    internal static LocalDateTime? Parse(string? raw)
        => Pattern.Parse(raw!) is { Success: true } result ? result.Value : null;
}
