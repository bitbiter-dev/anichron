using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
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
    // Dimensions — EXIF first, QuickTime track header as the fallback
    // ==========================================================================

    [Fact]
    public void BuildExifData_PositiveExifDimensions_AreUsedOverTheTrackHeader()
    {
        var result = Map(
            Ifd0((ExifDirectoryBase.TagImageWidth, 4032), (ExifDirectoryBase.TagImageHeight, 3024)),
            Track((QuickTimeTrackHeaderDirectory.TagWidth, 1920), (QuickTimeTrackHeaderDirectory.TagHeight, 1080)));

        (result.Width, result.Height).Should().Be((4032, 3024));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildExifData_NonPositiveExifDimension_FallsBackToTheTrackHeader(int exifWidth)
    {
        var result = Map(
            Ifd0((ExifDirectoryBase.TagImageWidth, exifWidth)),
            Track((QuickTimeTrackHeaderDirectory.TagWidth, 1920)));

        result.Width.Should().Be(1920);
    }

    [Fact]
    public void BuildExifData_OnlyTrackHeader_UsesIt()
        => Map(Track((QuickTimeTrackHeaderDirectory.TagHeight, 1080))).Height.Should().Be(1080);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildExifData_NonPositiveTrackHeaderDimension_IsZero(int trackWidth)
        => Map(Track((QuickTimeTrackHeaderDirectory.TagWidth, trackWidth))).Width.Should().Be(0);

    [Fact]
    public void BuildExifData_NoDimensionsAnywhere_IsZero()
        => Map().Width.Should().Be(0);

    // ==========================================================================
    // Video duration
    // ==========================================================================

    private static ExifData Duration(long duration, int timeScale)
        => Map(Movie((QuickTimeMovieHeaderDirectory.TagDuration, duration), (QuickTimeMovieHeaderDirectory.TagTimeScale, timeScale)));

    [Fact]
    public void BuildExifData_DurationAndTimeScale_RoundsToWholeSeconds()
        => Duration(duration: 2_500, timeScale: 1_000).DurationInSeconds.Should().Be(2);

    [Fact]
    public void BuildExifData_ZeroDuration_IsZeroSeconds()
        => Duration(duration: 0, timeScale: 600).DurationInSeconds.Should().Be(0);

    [Fact]
    public void BuildExifData_NegativeDuration_IsNull()
        => Duration(duration: -1, timeScale: 600).DurationInSeconds.Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-600)]
    public void BuildExifData_NonPositiveTimeScale_IsNull(int timeScale)
        => Duration(duration: 6_000, timeScale: timeScale).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_TimeScaleOfOne_IsAccepted()
        => Duration(duration: 7, timeScale: 1).DurationInSeconds.Should().Be(7);

    [Fact]
    public void BuildExifData_MovieHeaderWithoutTags_IsNull()
        => Map(Movie()).DurationInSeconds.Should().BeNull();

    [Fact]
    public void BuildExifData_NoMovieHeader_IsNull()
        => Map().DurationInSeconds.Should().BeNull();
}
