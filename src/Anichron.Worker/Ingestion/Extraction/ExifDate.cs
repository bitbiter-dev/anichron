using NodaTime;
using NodaTime.Text;

namespace Anichron.Worker.Ingestion.Extraction;

internal static class ExifDate
{
    // EXIF dates use colons as date separators ("2024:07:15 13:22:01"), unlike ISO 8601.
    private static LocalDateTimePattern Pattern
        => LocalDateTimePattern.CreateWithInvariantCulture("yyyy:MM:dd HH:mm:ss");

    private static OffsetPattern OffsetFormat => OffsetPattern.CreateWithInvariantCulture("+HH:mm");

    /// <summary>The date, or null when the raw value is missing or not an EXIF date.</summary>
    /// <param name="raw">An EXIF date-time tag's value.</param>
    internal static LocalDateTime? Parse(string? raw)
        => raw is { } text && Pattern.Parse(text) is { Success: true } result ? result.Value : null;

    /// <summary>The offset, or null when the raw value is not an EXIF offset ("+13:00").</summary>
    /// <param name="raw">An EXIF offset-time tag's value.</param>
    internal static Offset? ParseOffset(string raw)
        => OffsetFormat.Parse(raw) is { Success: true } result ? result.Value : null;
}
