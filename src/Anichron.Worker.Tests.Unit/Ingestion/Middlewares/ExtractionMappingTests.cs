using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using Anichron.Worker.Ingestion.Middlewares;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.QuickTime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

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
        Recorded(SubIfd(Original("2026:10:30 19:09:51"), OffsetOriginal(raw))).Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2026, 10, 30, 19, 9, 51), null));

        DebugLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("+25:00")]
    [InlineData("13:00")]
    [InlineData("garbage")]
    public void Extract_MalformedOffset_KeepsTheDateWithoutOffsetAndLogsIt(string raw)
    {
        Recorded(SubIfd(Original("2026:10:30 19:09:51"), OffsetOriginal(raw)))
            .Should().Be(new RecordedDate.ClockTime(new LocalDateTime(2026, 10, 30, 19, 9, 51), null));

        DebugLogs.Should().ContainSingle().Which.Should().Contain(raw);
    }

    [Fact]
    public void Extract_OffsetWithoutDate_IsUndated()
        => Recorded(SubIfd(OffsetOriginal("+13:00"))).Should().BeNull();

    [Theory]
    [InlineData("2024-07-15 13:22:01")]
    [InlineData("not a date")]
    public void Extract_UnparseableDate_IsUndatedAndLogged(string raw)
    {
        Recorded(SubIfd(Original(raw), OffsetOriginal("+13:00"))).Should().BeNull();

        DebugLogs.Should().ContainSingle().Which.Should().Contain(raw);
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

    private static (int, object) Created(DateTime created) => (QuickTimeMovieHeaderDirectory.TagCreated, created);

    private static DateTime Unspecified(int year, int month, int day, int hour, int minute, int second)
        => new(year, month, day, hour, minute, second, DateTimeKind.Unspecified);

    [Fact]
    public void Extract_VideoWithAMovieHeaderCreated_IsDatedByThatMomentInUtc()
        => Recorded(MediaType.Video, Movie(Created(Unspecified(2026, 10, 30, 6, 10, 45))))
            .Should().Be(new RecordedDate.Moment(Instant.FromUtc(2026, 10, 30, 6, 10, 45)));

    [Fact]
    public void Extract_VideoWithAZeroMovieHeaderCreated_IsUndated()
        => Recorded(MediaType.Video, Movie(Created(Unspecified(1904, 1, 1, 0, 0, 0)))).Should().BeNull();

    [Fact]
    public void Extract_VideoCreatedJustAfterTheQuickTimeEpoch_IsDated()
        => Recorded(MediaType.Video, Movie(Created(Unspecified(1904, 1, 1, 0, 0, 1))))
            .Should().Be(new RecordedDate.Moment(Instant.FromUtc(1904, 1, 1, 0, 0, 1)));

    [Fact]
    public void Extract_VideoWithoutAMovieHeaderCreated_IsUndated()
        => Recorded(MediaType.Video, Movie((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.FromSeconds(4)))).Should().BeNull();

    [Fact]
    public void Extract_VideoWithAnAppleCreationDate_IsUndatedUntilItsClockTimeIsRead()
        => Recorded(
                MediaType.Video,
                Movie(Created(Unspecified(2026, 10, 30, 6, 10, 45))),
                With(new QuickTimeMetadataHeaderDirectory(),
                    (QuickTimeMetadataHeaderDirectory.TagCreationDate, new DateTime(2026, 10, 30, 7, 10, 45, DateTimeKind.Local))))
            .Should().BeNull();

    [Fact]
    public void Extract_VideoWithOtherAppleMetadata_IsDatedByItsMovieHeader()
        => Recorded(
                MediaType.Video,
                Movie(Created(Unspecified(2026, 10, 30, 6, 10, 45))),
                With(new QuickTimeMetadataHeaderDirectory(), (QuickTimeMetadataHeaderDirectory.TagMake, "Apple")))
            .Should().Be(new RecordedDate.Moment(Instant.FromUtc(2026, 10, 30, 6, 10, 45)));

    [Fact]
    public void Extract_Photo_IsNotDatedByAMovieHeader()
        => Recorded(MediaType.Image, Movie(Created(Unspecified(2026, 10, 30, 6, 10, 45)))).Should().BeNull();

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

    private static QuickTimeMetadataHeaderDirectory Location(string iso6709)
        => With(new QuickTimeMetadataHeaderDirectory(),
            (QuickTimeMetadataHeaderDirectory.TagGpsLocation, new StringValue(Encoding.UTF8.GetBytes(iso6709), Encoding.UTF8)));

    [Theory]
    [InlineData("-36.8485+174.7633+012.000/", -36.8485, 174.7633)]
    [InlineData("+35.6895+139.6917/", 35.6895, 139.6917)]
    [InlineData("+52.52-013.405-5/", 52.52, -13.405)]
    [InlineData("+35+139/", 35.0, 139.0)]
    public void Extract_VideoLocation_ReadsCoordinates(string iso6709, double latitude, double longitude)
    {
        var result = Map(Location(iso6709));

        (result.Latitude, result.Longitude).Should().Be((latitude, longitude));
        DebugLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("+35.6895+139.6917")]
    [InlineData("x+35.6895+139.6917/")]
    [InlineData("+35.6895+139.6917/x")]
    [InlineData("35.6895+139.6917/")]
    [InlineData("+5.6895+139.6917/")]
    [InlineData("+35.6895+39.6917/")]
    [InlineData("+35.+139.6917/")]
    [InlineData("+35.6895+139.6917+/")]
    [InlineData("+4044.30-07400.20/")]
    [InlineData("+404430-0740020/")]
    [InlineData("")]
    public void Extract_MalformedVideoLocation_IsNoPositionAndLogged(string iso6709)
    {
        var result = Map(Location(iso6709));

        (result.Latitude, result.Longitude).Should().Be((null, null));
        DebugLogs.Should().ContainSingle().Which.Should().Be($"No position: malformed ISO 6709 location '{iso6709}'.");
    }

    [Fact]
    public void Extract_QuickTimeMetadataWithoutLocation_IsNoPosition()
    {
        var result = Map(With(new QuickTimeMetadataHeaderDirectory(), (QuickTimeMetadataHeaderDirectory.TagMake, "Apple")));

        (result.Latitude, result.Longitude).Should().Be((null, null));
        DebugLogs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_VideoWithAnAppleCreationDate_KeepsItsPosition()
    {
        var location = Location("-36.8485+174.7633+012.000/");
        location.Set(QuickTimeMetadataHeaderDirectory.TagCreationDate, new DateTime(2026, 10, 30, 7, 10, 45, DateTimeKind.Local));

        var result = Middleware.Extract([location], MediaType.Video);

        (result.Recorded, result.Description.Latitude, result.Description.Longitude).Should().Be((null, -36.8485, 174.7633));
    }

    [Fact]
    public void Extract_ExifGpsAndVideoLocation_TheExifGpsWins()
        => Map(Gps(51.5, 0.25), Location("-36.8485+174.7633/")).Latitude.Should().BeApproximately(51.5, 1e-6);

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
