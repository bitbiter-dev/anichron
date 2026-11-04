using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.QuickTime;
using System.Globalization;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

internal static class Durations
{
    public static int? Of(IReadOnlyList<Directory> directories)
        => OfMovieHeader(directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault())
            ?? OfAviHeader(directories.OfType<AviDirectory>().FirstOrDefault());

    private static int? OfMovieHeader(QuickTimeMovieHeaderDirectory? movieHeader)
        => movieHeader?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration && duration >= TimeSpan.Zero
            ? (int)Math.Round(duration.TotalSeconds)
            : null;

    private static int? OfAviHeader(AviDirectory? aviHeader)
        => aviHeader?.GetString(AviDirectory.TagDuration)?.Split(':') is [var hours, var minutes, var seconds]
           && TryParseWholeNumber(hours, out var h)
           && TryParseWholeNumber(minutes, out var m)
           && TryParseWholeNumber(seconds, out var s)
            ? (h * 3600) + (m * 60) + s
            : null;

    private static bool TryParseWholeNumber(string text, out int value)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
