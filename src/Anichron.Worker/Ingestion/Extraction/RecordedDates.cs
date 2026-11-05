using Anichron.Core.Domain;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using NodaTime;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>Reads the recorded capture date from the sources of a media type, in ADR 0015's order; the first wins.</summary>
internal static partial class RecordedDates
{
    private static DateTime QuickTimeEpoch => new(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private delegate RecordedDate? Source(IReadOnlyList<Directory> directories, ILogger logger);

    public static RecordedDate? Of(IReadOnlyList<Directory> directories, MediaType mediaType, ILogger logger)
        => SourcesOf(mediaType).Select(source => source(directories, logger)).FirstOrDefault(date => date is not null);

    private static Source[] SourcesOf(MediaType mediaType)
        => mediaType == MediaType.Video ? [MovieHeaderCreated] : [ExifOriginal];

    private static RecordedDate? ExifOriginal(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var raw = subIfd?.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        if (raw is null)
            return null;
        if (ExifDate.Parse(raw) is not { } at)
        {
            Log.DateParseFailed(logger, raw);
            return null;
        }

        return new RecordedDate.ClockTime(at, OffsetOf(subIfd!.GetString(ExifDirectoryBase.TagTimeZoneOriginal), logger));
    }

    private static RecordedDate? MovieHeaderCreated(IReadOnlyList<Directory> directories, ILogger logger)
    {
        if (HasAppleCreationDate(directories))
            return null;

        var created = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault()?.GetObject(QuickTimeMovieHeaderDirectory.TagCreated);
        return created is DateTime moment && moment != QuickTimeEpoch
            ? new RecordedDate.Moment(Instant.FromDateTimeUtc(DateTime.SpecifyKind(moment, DateTimeKind.Utc)))
            : null;
    }

    private static bool HasAppleCreationDate(IReadOnlyList<Directory> directories)
        => directories.OfType<QuickTimeMetadataHeaderDirectory>().Any(d => d.ContainsTag(QuickTimeMetadataHeaderDirectory.TagCreationDate));

    private static Offset? OffsetOf(string? raw, ILogger logger)
    {
        if (raw is null || raw.Trim(' ', ':').Length == 0)
            return null;
        if (ExifDate.ParseOffset(raw) is { } offset)
            return offset;

        Log.OffsetParseFailed(logger, raw);
        return null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse EXIF date '{Raw}'.")]
        public static partial void DateParseFailed(ILogger logger, string raw);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse EXIF offset '{Raw}'; the date keeps no offset.")]
        public static partial void OffsetParseFailed(ILogger logger, string raw);
    }
}
