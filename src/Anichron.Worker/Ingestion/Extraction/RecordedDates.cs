using Anichron.Core.Domain;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using NodaTime;
using NodaTime.Text;
using XmpCore;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>Reads the recorded capture date from the sources of a media type, in ADR 0015's order; the first wins.</summary>
internal static partial class RecordedDates
{
    private const int shortestXmpClock = 16;
    private const int iptcClock = 6;

    private delegate RecordedDate? Source(IReadOnlyList<Directory> directories, ILogger logger);

    private static LocalDatePattern XmpDay => LocalDatePattern.Iso;
    private static LocalDateTimePattern XmpClock => LocalDateTimePattern.ExtendedIso;
    private static OffsetPattern XmpOffset => OffsetPattern.CreateWithInvariantCulture("Z+HH:mm");
    private static LocalDatePattern IptcDay => LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");
    private static LocalTimePattern IptcTime => LocalTimePattern.CreateWithInvariantCulture("HHmmss");
    private static OffsetPattern IptcOffset => OffsetPattern.CreateWithInvariantCulture("+HHmm");
    private static LocalDateTimePattern AviClock => LocalDateTimePattern.CreateWithInvariantCulture("ddd MMM d HH':'mm':'ss uuuu");

    public static RecordedDate? Of(IReadOnlyList<Directory> directories, MediaType mediaType, ILogger logger)
        => SourcesOf(mediaType).Select(source => source(directories, logger)).FirstOrDefault(date => date is not null);

    private static Source[] SourcesOf(MediaType mediaType)
        => mediaType == MediaType.Video ? [AviOriginal] : [ExifOriginal, XmpDateCreated, IptcDateCreated];

    private static RecordedDate? ExifOriginal(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var raw = subIfd?.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        if (raw is null)
            return null;
        if (ExifDate.Parse(raw) is not { } at)
            return Unparseable("EXIF", raw, logger);

        return new RecordedDate.ClockTime(at, OffsetOf("EXIF", subIfd!.GetString(ExifDirectoryBase.TagTimeZoneOriginal), ExifDate.ParseOffset, logger));
    }

    private static RecordedDate? XmpDateCreated(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var raw = directories.OfType<XmpDirectory>().FirstOrDefault()?.XmpMeta?.GetPropertyString(XmpConstants.NsPhotoshop, "DateCreated");
        if (raw is null)
            return null;
        if (Parsed(XmpDay, raw) is { } day)
            return new RecordedDate.DateAlone(day);

        var (clock, offset) = SplitOffset(raw, shortestXmpClock);
        if (Parsed(XmpClock, clock.Length == shortestXmpClock ? clock + ":00" : clock) is not { } at)
            return Unparseable("XMP", raw, logger);

        return new RecordedDate.ClockTime(at, OffsetOf("XMP", offset, text => Parsed(XmpOffset, text), logger));
    }

    private static RecordedDate? IptcDateCreated(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
        var raw = iptc?.GetString(IptcDirectory.TagDateCreated);
        if (raw is null)
            return null;
        if (Parsed(IptcDay, raw) is not { } day)
            return Unparseable("IPTC", raw, logger);
        if (iptc!.GetString(IptcDirectory.TagTimeCreated) is not { } rawTime)
            return new RecordedDate.DateAlone(day);

        var (clock, offset) = SplitOffset(rawTime, iptcClock);
        if (Parsed(IptcTime, clock) is not { } time)
            return Unparseable("IPTC", rawTime, logger);

        return new RecordedDate.ClockTime(day + time, OffsetOf("IPTC", offset, text => Parsed(IptcOffset, text), logger));
    }

    private static RecordedDate? AviOriginal(IReadOnlyList<Directory> directories, ILogger logger)
    {
        var raw = directories.OfType<AviDirectory>().FirstOrDefault()?.GetString(AviDirectory.TagDateTimeOriginal);
        if (raw is null)
            return null;
        if (Parsed(AviClock, string.Join(' ', raw.Split([' ', '\n', '\0'], StringSplitOptions.RemoveEmptyEntries))) is not { } at)
            return Unparseable("AVI", raw, logger);

        return new RecordedDate.ClockTime(at, null);
    }

    private static (string Clock, string? Offset) SplitOffset(string raw, int shortestClock)
        => raw.LastIndexOfAny(['Z', '+', '-']) is var start && start >= shortestClock ? (raw[..start], raw[start..]) : (raw, null);

    private static Offset? OffsetOf(string source, string? raw, Func<string, Offset?> parse, ILogger logger)
    {
        if (raw is null || raw.Trim(' ', ':').Length == 0)
            return null;
        if (parse(raw) is { } offset)
            return offset;

        Log.OffsetParseFailed(logger, source, raw);
        return null;
    }

    private static T? Parsed<T>(IPattern<T> pattern, string raw) where T : struct
        => pattern.Parse(raw) is { Success: true } result ? result.Value : null;

    private static RecordedDate? Unparseable(string source, string raw, ILogger logger)
    {
        Log.DateParseFailed(logger, source, raw);
        return null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse {Source} date '{Raw}'.")]
        public static partial void DateParseFailed(ILogger logger, string source, string raw);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse {Source} offset '{Raw}'; the date keeps no offset.")]
        public static partial void OffsetParseFailed(ILogger logger, string source, string raw);
    }
}
