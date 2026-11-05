using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using Anichron.Worker.Ingestion.Middlewares;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;
using XmpCore;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ExtractionMappingTests
{
    private readonly FakeLogger<ExifExtractionMiddleware> logger = new();

    private ExifExtractionMiddleware Middleware => new(new MockFileSystem(), logger);

    private static T With<T>(T directory, params (int Tag, object Value)[] tags) where T : MetadataExtractor.Directory
    {
        foreach (var (tag, value) in tags)
            directory.Set(tag, value);
        return directory;
    }

    private static ExifIfd0Directory Ifd0(params (int Tag, object Value)[] tags) => With(new ExifIfd0Directory(), tags);
    private static ExifSubIfdDirectory SubIfd(params (int Tag, object Value)[] tags) => With(new ExifSubIfdDirectory(), tags);
    private static QuickTimeTrackHeaderDirectory Track(params (int Tag, object Value)[] tags) => With(new QuickTimeTrackHeaderDirectory(), tags);
    private static QuickTimeMovieHeaderDirectory Movie(params (int Tag, object Value)[] tags) => With(new QuickTimeMovieHeaderDirectory(), tags);
    private static AviDirectory Avi(params (int Tag, object Value)[] tags) => With(new AviDirectory(), tags);

    private static GpsDirectory Gps(double latitude, double longitude)
    {
        static Rational[] Dms(double degrees) => [new((long)(degrees * 1_000_000), 1_000_000), new(0, 1), new(0, 1)];
        return With(new GpsDirectory(),
            (GpsDirectory.TagLatitudeRef, "N"), (GpsDirectory.TagLatitude, Dms(latitude)),
            (GpsDirectory.TagLongitudeRef, "E"), (GpsDirectory.TagLongitude, Dms(longitude)));
    }

    private MediaDescription Map(params MetadataExtractor.Directory[] directories) => Middleware.Extract(directories, MediaType.Image).Description;

    private RecordedDate? Recorded(MediaType mediaType, params MetadataExtractor.Directory[] directories)
        => Middleware.Extract(directories, mediaType).Recorded;

    private RecordedDate? Recorded(params MetadataExtractor.Directory[] directories) => Recorded(MediaType.Image, directories);

    private IEnumerable<string> DebugLogs
        => logger.Collector.GetSnapshot().Where(r => r.Level == LogLevel.Debug).Select(r => r.Message);

    [Fact]
    public void CanInvoke_AlwaysTrue()
        => Middleware.CanInvoke(null!).Should().BeTrue();

    // ==========================================================================
    // Camera, lens and capture date
    // ==========================================================================

    [Fact]
    public void Extract_NoIfd0Directory_StillReadsTheSubIfd()
    {
        var result = Map(SubIfd((ExifDirectoryBase.TagLensModel, "50mm")));

        result.LensModel.Should().Be("50mm");
        result.CameraMake.Should().BeNull();
    }

    [Fact]
    public void Extract_CameraAndLens_AreRead()
    {
        var result = Map(
            Ifd0((ExifDirectoryBase.TagMake, "Apple"), (ExifDirectoryBase.TagModel, "iPhone")),
            SubIfd((ExifDirectoryBase.TagLensModel, "Wide")));

        (result.CameraMake, result.CameraModel, result.LensModel).Should().Be(("Apple", "iPhone", "Wide"));
    }

    private static (int, object) Original(string raw) => (ExifDirectoryBase.TagDateTimeOriginal, raw);

    private static (int, object) OffsetOriginal(string raw) => (ExifDirectoryBase.TagTimeZoneOriginal, raw);

    [Fact]
    public void Extract_ExifColonSeparatedDate_IsParsedWithoutOffset()
        => Recorded(SubIfd(Original("2024:07:15 13:22:01"))).Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2024, 7, 15, 13, 22, 1), null));

    [Theory]
    [InlineData("+13:00", 13 * 3600)]
    [InlineData("-03:30", -12600)]
    [InlineData("+00:00", 0)]
    public void Extract_OffsetTimeOriginal_IsTheDatesOffset(string raw, int seconds)
        => Recorded(SubIfd(Original("2026:10:30 19:09:51"), OffsetOriginal(raw)))
            .Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2026, 10, 30, 19, 9, 51), Offset.FromSeconds(seconds)));

    [Theory]
    [InlineData("   :  ")]
    [InlineData("")]
    public void Extract_BlankOffset_IsNoOffsetAndNotLogged(string raw)
    {
        Recorded(SubIfd(Original("2026:10:30 19:09:51"), OffsetOriginal(raw)))
            .Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2026, 10, 30, 19, 9, 51), null));

        DebugLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("+25:00")]
    [InlineData("13:00")]
    [InlineData("+13")]
    [InlineData("Z")]
    [InlineData("garbage")]
    public void Extract_MalformedOffset_KeepsTheDateWithoutOffsetAndLogsIt(string raw)
    {
        Recorded(SubIfd(Original("2026:10:30 19:09:51"), OffsetOriginal(raw)))
            .Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2026, 10, 30, 19, 9, 51), null));

        DebugLogs.Should().ContainSingle().Which.Should().Contain(raw).And.Contain("EXIF");
    }

    [Fact]
    public void Extract_OffsetWithoutDate_IsUndated()
        => Recorded(SubIfd(OffsetOriginal("+13:00"))).Should().BeNull();

    [Theory]
    [InlineData("2024-07-15 13:22:01")]
    [InlineData("    :  :     :  :  ")]
    [InlineData("not a date")]
    public void Extract_UnparseableDate_IsUndatedAndLogged(string raw)
    {
        Recorded(SubIfd(Original(raw), OffsetOriginal("+13:00"))).Should().BeNull();

        DebugLogs.Should().ContainSingle().Which.Should().Contain(raw).And.Contain("EXIF");
    }

    [Fact]
    public void Extract_NoDateTag_IsUndated()
        => Recorded(SubIfd()).Should().BeNull();

    [Fact]
    public void Extract_LivePhoto_IsDatedLikeAPhoto()
        => Recorded(MediaType.LivePhoto, SubIfd(Original("2024:07:15 13:22:01"))).Should().NotBeNull();

    [Fact]
    public void Extract_Video_IsUndatedWhateverItsExifSays()
        => Recorded(MediaType.Video, SubIfd(Original("2024:07:15 13:22:01"))).Should().BeNull();

    [Fact]
    public void Extract_OnlyDateTimeDigitized_IsUndated()
        => Recorded(SubIfd((ExifDirectoryBase.TagDateTimeDigitized, "2024:07:15 13:22:01"))).Should().BeNull();

    // ==========================================================================
    // XMP and IPTC "date created"
    // ==========================================================================

    private static XmpDirectory Xmp(string schema, string property, string value)
    {
        var meta = XmpMetaFactory.Create();
        meta.SetProperty(schema, property, value);
        var directory = new XmpDirectory();
        directory.SetXmpMeta(meta);
        return directory;
    }

    private static XmpDirectory XmpCreated(string raw) => Xmp(XmpConstants.NsPhotoshop, "DateCreated", raw);

    private static IptcDirectory Iptc(string? date, string? time = null)
    {
        var directory = new IptcDirectory();
        if (date is not null)
            directory.Set(IptcDirectory.TagDateCreated, date);
        if (time is not null)
            directory.Set(IptcDirectory.TagTimeCreated, time);
        return directory;
    }

    private static RecordedDate.ClockTime At(int hour, int minute, int second = 0, int millisecond = 0, int? offsetMinutes = null)
        => new(new LocalDateTime(2019, 5, 4, hour, minute, second, millisecond), offsetMinutes is { } minutes ? Offset.FromSeconds(minutes * 60) : null);

    [Theory]
    [InlineData("2019-05-04T13:22:01+13:00", 13, 22, 1, 0, 13 * 60)]
    [InlineData("2019-05-04T13:22+13:00", 13, 22, 0, 0, 13 * 60)]
    [InlineData("2019-05-04T13:22:01.25-03:30", 13, 22, 1, 250, -210)]
    [InlineData("2019-05-04T13:22:01Z", 13, 22, 1, 0, 0)]
    [InlineData("2019-05-04T13:22:01", 13, 22, 1, 0, null)]
    [InlineData("2019-05-04T13:22", 13, 22, 0, 0, null)]
    public void Extract_XmpDateCreatedWithATime_IsThatClockTimeWithTheOffsetWritten(
        string raw, int hour, int minute, int second, int millisecond, int? offsetMinutes)
    {
        Recorded(XmpCreated(raw)).Should().Be(At(hour, minute, second, millisecond, offsetMinutes));

        DebugLogs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_XmpDateCreatedWithAFullDateAlone_IsADateAlone()
        => Recorded(XmpCreated("2019-05-04")).Should().Be(new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)));

    [Theory]
    [InlineData("2019")]
    [InlineData("2019-05")]
    [InlineData("2019-05-04T")]
    [InlineData("2019:05:04 13:22:01")]
    [InlineData("2019-05-04T13:22:01 +13:00")]
    public void Extract_XmpDateCreatedShorterThanAFullDateOrMalformed_IsUndatedAndLogged(string raw)
    {
        Recorded(XmpCreated(raw)).Should().BeNull();

        DebugLogs.Should().ContainSingle().Which.Should().Contain($"'{raw}'").And.Contain("XMP");
    }

    [Theory]
    [InlineData("+25:00")]
    [InlineData("+1300")]
    public void Extract_XmpDateCreatedWithAMalformedOffset_KeepsTheTimeWithoutOffsetAndLogsIt(string offset)
    {
        Recorded(XmpCreated($"2019-05-04T13:22:01{offset}")).Should().Be(At(13, 22, 1));

        DebugLogs.Should().ContainSingle().Which.Should().Contain($"'{offset}'").And.Contain("XMP");
    }

    [Fact]
    public void Extract_OnlyXmpCreateDate_IsUndated()
        => Recorded(Xmp(XmpConstants.NsXmp, "CreateDate", "2019-05-04T13:22:01+13:00")).Should().BeNull();

    [Theory]
    [InlineData("132201+1300", 13, 22, 1, 13 * 60)]
    [InlineData("065900-0330", 6, 59, 0, -210)]
    [InlineData("132201", 13, 22, 1, null)]
    public void Extract_IptcDateAndTimeCreated_IsThatClockTimeWithTheOffsetWritten(string time, int hour, int minute, int second, int? offsetMinutes)
    {
        Recorded(Iptc("20190504", time)).Should().Be(At(hour, minute, second, offsetMinutes: offsetMinutes));

        DebugLogs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_IptcDateCreatedWithoutTimeCreated_IsADateAlone()
        => Recorded(Iptc("20190504")).Should().Be(new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)));

    [Fact]
    public void Extract_IptcTimeCreatedWithoutDateCreated_IsUndatedAndNotLogged()
    {
        Recorded(Iptc(null, "132201+1300")).Should().BeNull();

        DebugLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("20190500")]
    [InlineData("2019-05-04")]
    public void Extract_UnparseableIptcDateCreated_IsUndatedAndLogged(string date)
    {
        Recorded(Iptc(date, "132201+1300")).Should().BeNull();

        DebugLogs.Should().ContainSingle().Which.Should().Contain($"'{date}'").And.Contain("IPTC");
    }

    [Theory]
    [InlineData("1322")]
    [InlineData("13:22:01")]
    [InlineData("13-2201")]
    public void Extract_UnparseableIptcTimeCreated_IsUndatedAndLogged(string time)
    {
        Recorded(Iptc("20190504", time)).Should().BeNull();

        DebugLogs.Should().ContainSingle().Which.Should().Contain($"'{time}'").And.Contain("IPTC");
    }

    [Fact]
    public void Extract_IptcTimeCreatedWithAMalformedOffset_KeepsTheTimeWithoutOffsetAndLogsIt()
    {
        Recorded(Iptc("20190504", "132201+2500")).Should().Be(At(13, 22, 1));

        DebugLogs.Should().ContainSingle().Which.Should().Contain("'+2500'").And.Contain("IPTC");
    }

    [Fact]
    public void Extract_ExifDateTimeOriginal_WinsOverXmpAndIptc()
        => Recorded(SubIfd(Original("2024:07:15 13:22:01")), XmpCreated("2019-05-04T13:22:01"), Iptc("20190504", "132201"))
            .Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2024, 7, 15, 13, 22, 1), null));

    [Fact]
    public void Extract_XmpDateCreated_WinsOverIptc()
        => Recorded(XmpCreated("2019-05-04T13:22:01"), Iptc("20180101", "090000")).Should().Be(At(13, 22, 1));

    [Fact]
    public void Extract_XmpDateAlone_WinsOverIptcWithATime()
        => Recorded(XmpCreated("2019-05-04"), Iptc("20190504", "132201")).Should().Be(new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)));

    [Fact]
    public void Extract_UnparseableExifAndXmp_FallThroughToIptc()
        => Recorded(SubIfd(Original("not a date")), XmpCreated("2019"), Iptc("20190504", "132201")).Should().Be(At(13, 22, 1));

    [Fact]
    public void Extract_LivePhoto_IsDatedByXmpAndIptcLikeAPhoto()
        => Recorded(MediaType.LivePhoto, Iptc("20190504", "132201")).Should().Be(At(13, 22, 1));

    [Fact]
    public void Extract_Video_IsUndatedWhateverItsXmpOrIptcSays()
        => Recorded(MediaType.Video, XmpCreated("2019-05-04T13:22:01"), Iptc("20190504", "132201")).Should().BeNull();

    // ==========================================================================
    // AVI "date/time original"
    // ==========================================================================

    private RecordedDate? AviRecorded(MediaType mediaType, string raw)
        => Recorded(mediaType, Avi((AviDirectory.TagDateTimeOriginal, raw)));

    [Theory]
    [InlineData("MON SEP 10 20:52:00 2012", 2012, 9, 10, 20, 52, 0)]
    [InlineData("Wed Nov  3 16:12:40 2004\n\0", 2004, 11, 3, 16, 12, 40)]
    public void Extract_AviDateTimeOriginal_IsThatClockTimeWithNoOffset(string raw, int year, int month, int day, int hour, int minute, int second)
        => AviRecorded(MediaType.Video, raw)
            .Should().Be(new RecordedDate.ClockTime(new LocalDateTime(year, month, day, hour, minute, second), null));

    [Theory]
    [InlineData("2012:09:10 20:52:00")]
    [InlineData("TUE SEP 10 20:52:00 2012")]
    public void Extract_UnparseableAviDateTimeOriginal_IsUndatedAndLogged(string raw)
    {
        AviRecorded(MediaType.Video, raw).Should().BeNull();

        DebugLogs.Should().ContainSingle().Which.Should().Contain($"'{raw}'").And.Contain("AVI");
    }

    [Fact]
    public void Extract_AviWithoutDateTimeOriginal_IsUndated()
        => Recorded(MediaType.Video, Avi((AviDirectory.TagDuration, "00:00:03"))).Should().BeNull();

    [Fact]
    public void Extract_Photo_IsNotDatedByAnAviDate()
        => AviRecorded(MediaType.Image, "MON SEP 10 20:52:00 2012").Should().BeNull();

    // ==========================================================================
    // GPS
    // ==========================================================================

    [Fact]
    public void Extract_GpsPresent_ReadsCoordinates()
    {
        var result = Map(Gps(51.5, 0.25));

        result.Latitude.Should().BeApproximately(51.5, 1e-6);
        result.Longitude.Should().BeApproximately(0.25, 1e-6);
    }

    [Fact]
    public void Extract_GpsDirectoryWithoutCoordinates_IsNull()
    {
        var result = Map(new GpsDirectory());

        result.Latitude.Should().BeNull();
        result.Longitude.Should().BeNull();
    }

    [Fact]
    public void Extract_NoGpsDirectory_IsNull()
        => Map().Latitude.Should().BeNull();

    // ==========================================================================
    // Size — the first directory with a positive width and height
    // ==========================================================================

    private (int, int) Size(params MetadataExtractor.Directory[] directories)
    {
        var result = Map(directories);
        return (result.Width, result.Height);
    }

    private static QuickTimeTrackHeaderDirectory SizedTrack(int width, int height)
        => Track((QuickTimeTrackHeaderDirectory.TagWidth, width), (QuickTimeTrackHeaderDirectory.TagHeight, height));

    [Theory]
    [InlineData(0, 3024)]
    [InlineData(-1, 3024)]
    [InlineData(4032, 0)]
    [InlineData(4032, -1)]
    public void Extract_NonPositiveDimension_FallsThroughToTheNextDirectory(int width, int height)
        => Size(Ifd0((ExifDirectoryBase.TagImageWidth, width), (ExifDirectoryBase.TagImageHeight, height)), SizedTrack(1920, 1080))
            .Should().Be((1920, 1080));

    [Fact]
    public void Extract_OnlyAWidth_FallsThroughToTheNextDirectory()
        => Size(Ifd0((ExifDirectoryBase.TagImageWidth, 4032)), SizedTrack(1920, 1080)).Should().Be((1920, 1080));

    [Fact]
    public void Extract_NoDimensionsAnywhere_IsZero()
        => Size().Should().Be((0, 0));

    // ==========================================================================
    // Orientation — the HEIC container, else the video track, else EXIF
    // ==========================================================================

    private static HeicImagePropertiesDirectory HeicPrimary(params (int Tag, object Value)[] tags)
        => With(new HeicImagePropertiesDirectory("HEIC Primary Item Properties"), tags);

    private static ExifIfd0Directory TurnedExif() => Ifd0((ExifDirectoryBase.TagOrientation, 6));

    [Fact]
    public void Extract_VideoRotation_IsTheOrientation()
        => Map(Track(
                (QuickTimeTrackHeaderDirectory.TagWidth, 1920),
                (QuickTimeTrackHeaderDirectory.TagHeight, 1080),
                (QuickTimeTrackHeaderDirectory.TagRotation, 180.0)))
            .Orientation.Should().Be(3);

    [Fact]
    public void Extract_VideoTrackWithoutRotation_IsUprightWhateverExifSays()
        => Map(SizedTrack(1920, 1080), TurnedExif()).Orientation.Should().Be(1);

    [Fact]
    public void Extract_HeicWithoutIrot_IsUprightWhateverExifSays()
        => Map(HeicPrimary(), TurnedExif()).Orientation.Should().Be(1);

    [Fact]
    public void Extract_HeicIrot_WinsOverTheVideoTrack()
        => Map(HeicPrimary((HeicImagePropertiesDirectory.TagRotation, 180u)), SizedTrack(1920, 1080)).Orientation.Should().Be(3);

    [Fact]
    public void Extract_HeicThumbnailIrot_IsIgnored()
        => Map(With(new HeicImagePropertiesDirectory("HEIC Thumbnail Properties"), (HeicImagePropertiesDirectory.TagRotation, 270u)))
            .Orientation.Should().Be(1);

    // ==========================================================================
    // Video duration
    // ==========================================================================

    private MediaDescription Duration(object duration)
        => Map(Movie((QuickTimeMovieHeaderDirectory.TagDuration, duration)));

    [Fact]
    public void Extract_Duration_RoundsToWholeSeconds()
        => Duration(TimeSpan.FromSeconds(2.5)).DurationInSeconds.Should().Be(2);

    [Fact]
    public void Extract_ZeroDuration_IsZeroSeconds()
        => Duration(TimeSpan.Zero).DurationInSeconds.Should().Be(0);

    [Fact]
    public void Extract_NegativeDuration_IsNull()
        => Duration(TimeSpan.FromSeconds(-1)).DurationInSeconds.Should().BeNull();

    [Fact]
    public void Extract_DurationThatIsNotATimeSpan_IsNull()
        => Duration(6_000L).DurationInSeconds.Should().BeNull();

    [Fact]
    public void Extract_MovieHeaderWithoutTags_IsNull()
        => Map(Movie()).DurationInSeconds.Should().BeNull();

    [Fact]
    public void Extract_NoMovieHeader_IsNull()
        => Map().DurationInSeconds.Should().BeNull();

    [Theory]
    [InlineData("01:02:03", 3723)]
    [InlineData("25:00:00", 90000)]
    public void Extract_AviDuration_IsItsWholeSeconds(string raw, int seconds)
        => Map(Avi((AviDirectory.TagDuration, raw))).DurationInSeconds.Should().Be(seconds);

    [Theory]
    [InlineData("3")]
    [InlineData("00:03")]
    [InlineData("00:00:00:03")]
    [InlineData("00:00:03.5")]
    [InlineData("x:00:03")]
    [InlineData("00:x:03")]
    [InlineData("00:00:x")]
    [InlineData("00:-1:03")]
    public void Extract_AviDurationNotInHoursMinutesSeconds_IsNull(string raw)
        => Map(Avi((AviDirectory.TagDuration, raw))).DurationInSeconds.Should().BeNull();

    [Fact]
    public void Extract_AviWithoutDuration_IsNull()
        => Map(Avi()).DurationInSeconds.Should().BeNull();

    [Fact]
    public void Extract_MovieHeaderDuration_WinsOverAvi()
        => Map(Avi((AviDirectory.TagDuration, "00:00:09")), Movie((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.FromSeconds(4))))
            .DurationInSeconds.Should().Be(4);
}
