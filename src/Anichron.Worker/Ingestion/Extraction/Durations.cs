using MetadataExtractor.Formats.QuickTime;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

internal static class Durations
{
    public static int? Of(IReadOnlyList<Directory> directories)
        => directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault()?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration)
            is TimeSpan duration && duration >= TimeSpan.Zero
            ? (int)Math.Round(duration.TotalSeconds)
            : null;
}
