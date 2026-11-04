using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.QuickTime;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ExifDataMappingTests
{
    private static readonly ExifExtractionMiddleware Middleware
        = new(new MockFileSystem(), Substitute.For<ILogger<ExifExtractionMiddleware>>());

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

    private static ExifData Map(params MetadataExtractor.Directory[] directories) => Middleware.BuildExifData(directories);

    [Fact]
    public void CanInvoke_AlwaysTrue()
        => Middleware.CanInvoke(null!).Should().BeTrue();

    // ==========================================================================
    // Camera, lens and capture date
    // ==========================================================================

    [Fact]
    public void BuildExifData_NoIfd0Directory_StillReadsTheSubIfd()
    {
        var result = Map(SubIfd((ExifDirectoryBase.TagLensModel, "50mm")));

        result.LensModel.Should().Be("50mm");
        result.CameraMake.Should().BeNull();
    }

    [Fact]
    public void BuildExifData_CameraAndLens_AreRead()
    {
        var result = Map(
            Ifd0((ExifDirectoryBase.TagMake, "Apple"), (ExifDirectoryBase.TagModel, "iPhone")),
            SubIfd((ExifDirectoryBase.TagLensModel, "Wide")));

        (result.CameraMake, result.CameraModel, result.LensModel).Should().Be(("Apple", "iPhone", "Wide"));
    }

    [Fact]
    public void BuildExifData_ExifColonSeparatedDate_IsParsed()
    {
        var result = Map(SubIfd((ExifDirectoryBase.TagDateTimeOriginal, "2024:07:15 13:22:01")));

        result.DateCaptured.Should().Be(new LocalDateTime(2024, 7, 15, 13, 22, 1));
    }

    [Theory]
    [InlineData("2024-07-15 13:22:01")]
    [InlineData("not a date")]
    public void BuildExifData_UnparseableDate_IsNull(string raw)
        => Map(SubIfd((ExifDirectoryBase.TagDateTimeOriginal, raw))).DateCaptured.Should().BeNull();

    [Fact]
    public void BuildExifData_NoDateTag_IsNull()
        => Map(SubIfd()).DateCaptured.Should().BeNull();

    // ==========================================================================
    // GPS
    // ==========================================================================

    [Fact]
    public void BuildExifData_GpsPresent_ReadsCoordinates()
    {
        var result = Map(Gps(51.5, 0.25));

        result.Latitude.Should().BeApproximately(51.5, 1e-6);
        result.Longitude.Should().BeApproximately(0.25, 1e-6);
    }

    [Fact]
    public void BuildExifData_GpsDirectoryWithoutCoordinates_IsNull()
    {
        var result = Map(new GpsDirectory());

        result.Latitude.Should().BeNull();
        result.Longitude.Should().BeNull();
    }

    [Fact]
    public void BuildExifData_NoGpsDirectory_IsNull()
        => Map().Latitude.Should().BeNull();

    // ==========================================================================
    // Size — the first directory with a positive width and height
    // ==========================================================================

    private static (int, int) Size(params MetadataExtractor.Directory[] directories)
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
    public void BuildExifData_NonPositiveDimension_FallsThroughToTheNextDirectory(int width, int height)
        => Size(Ifd0((ExifDirectoryBase.TagImageWidth, width), (ExifDirectoryBase.TagImageHeight, height)), SizedTrack(1920, 1080))
            .Should().Be((1920, 1080));

    [Fact]
    public void BuildExifData_OnlyAWidth_FallsThroughToTheNextDirectory()
        => Size(Ifd0((ExifDirectoryBase.TagImageWidth, 4032)), SizedTrack(1920, 1080)).Should().Be((1920, 1080));

    [Fact]
    public void BuildExifData_NoDimensionsAnywhere_IsZero()
        => Size().Should().Be((0, 0));

    // ==========================================================================
    // Orientation — the HEIC container, else the video track, else EXIF
    // ==========================================================================

    private static HeicImagePropertiesDirectory HeicPrimary(params (int Tag, object Value)[] tags)
        => With(new HeicImagePropertiesDirectory("HEIC Primary Item Properties"), tags);

    private static ExifIfd0Directory TurnedExif() => Ifd0((ExifDirectoryBase.TagOrientation, 6));

    [Fact]
    public void BuildExifData_VideoRotation_IsTheOrientation()
        => Map(Track(
                (QuickTimeTrackHeaderDirectory.TagWidth, 1920),
                (QuickTimeTrackHeaderDirectory.TagHeight, 1080),
                (QuickTimeTrackHeaderDirectory.TagRotation, 180.0)))
            .Orientation.Should().Be(3);

    [Fact]
    public void BuildExifData_VideoTrackWithoutRotation_IsUprightWhateverExifSays()
        => Map(SizedTrack(1920, 1080), TurnedExif()).Orientation.Should().Be(1);

    [Fact]
    public void BuildExifData_HeicWithoutIrot_IsUprightWhateverExifSays()
        => Map(HeicPrimary(), TurnedExif()).Orientation.Should().Be(1);

    [Fact]
    public void BuildExifData_HeicIrot_WinsOverTheVideoTrack()
        => Map(HeicPrimary((HeicImagePropertiesDirectory.TagRotation, 180u)), SizedTrack(1920, 1080)).Orientation.Should().Be(3);

    [Fact]
    public void BuildExifData_HeicThumbnailIrot_IsIgnored()
        => Map(With(new HeicImagePropertiesDirectory("HEIC Thumbnail Properties"), (HeicImagePropertiesDirectory.TagRotation, 270u)))
            .Orientation.Should().Be(1);

    // ==========================================================================
    // Video duration
    // ==========================================================================

    private static ExifData Duration(object duration)
        => Map(Movie((QuickTimeMovieHeaderDirectory.TagDuration, duration)));

    [Fact]
    public void BuildExifData_Duration_RoundsToWholeSeconds()
        => Duration(TimeSpan.FromSeconds(2.5)).DurationInSeconds.Should().Be(2);

    [Fact]
    public void BuildExifData_ZeroDuration_IsZeroSeconds()
        => Duration(TimeSpan.Zero).DurationInSeconds.Should().Be(0);

    [Fact]
    public void BuildExifData_NegativeDuration_IsNull()
        => Duration(TimeSpan.FromSeconds(-1)).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_DurationThatIsNotATimeSpan_IsNull()
        => Duration(6_000L).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_MovieHeaderWithoutTags_IsNull()
        => Map(Movie()).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_NoMovieHeader_IsNull()
        => Map().DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_AviDuration_IsItsWholeSeconds()
        => Map(Avi((AviDirectory.TagDuration, "01:02:03"))).DurationInSeconds.Should().Be(3723);

    [Theory]
    [InlineData("3")]
    [InlineData("00:00:03.5")]
    [InlineData("24:00:00")]
    public void BuildExifData_AviDurationNotInHoursMinutesSeconds_IsNull(string raw)
        => Map(Avi((AviDirectory.TagDuration, raw))).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_AviWithoutDuration_IsNull()
        => Map(Avi()).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_MovieHeaderDuration_WinsOverAvi()
        => Map(Avi((AviDirectory.TagDuration, "00:00:09")), Movie((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.FromSeconds(4))))
            .DurationInSeconds.Should().Be(4);
}
