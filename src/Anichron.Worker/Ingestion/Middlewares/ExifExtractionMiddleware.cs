using Anichron.Worker.Ingestion.Pipeline;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using NodaTime;
using NodaTime.Text;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ExifExtractionMiddleware(
    IFileSystem fileSystem,
    ILogger<ExifExtractionMiddleware> logger) : IIngestionMiddleware
{
    // EXIF dates use colons as date separators ("2024:07:15 13:22:01"), unlike ISO 8601.
    private static readonly LocalDateTimePattern exifDatePattern =
        LocalDateTimePattern.CreateWithInvariantCulture("yyyy:MM:dd HH:mm:ss");

    public bool CanInvoke(IngestionContext context) => context.ContentHash is not null;

    public IngestionStepError OnCannotInvoke(IngestionContext context)
        => new("ContentHash must be set before EXIF extraction");

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        context.Exif = ExtractExif(context.Item.AbsolutePath);
        await next(context, ct);
    }

    private ExifData ExtractExif(string absolutePath)
    {
        try
        {
            using var stream = fileSystem.File.OpenRead(absolutePath);
            var metadataDirectories = ImageMetadataReader.ReadMetadata(stream);
            return BuildExifData(metadataDirectories);
        }
        catch (Exception ex)
        {
            Log.ExtractionFailed(logger, absolutePath, ex);
            return ExifData.Empty;
        }
    }

    private ExifData BuildExifData(IReadOnlyList<MetadataExtractor.Directory> metadataDirectories)
    {
        var cameraInfo = metadataDirectories.OfType<ExifIfd0Directory>().FirstOrDefault();     // IFD0: camera make/model, orientation
        var captureDetails = metadataDirectories.OfType<ExifSubIfdDirectory>().FirstOrDefault(); // Sub-IFD: date captured, lens model
        var gps = metadataDirectories.OfType<GpsDirectory>().FirstOrDefault();                // GPS coordinates
        var trackHeader = metadataDirectories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault(); // video dimensions
        var movieHeader = metadataDirectories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault(); // video duration

        var (latitude, longitude) = GetCoordinates(gps);

        return new ExifData(
            Width: GetDimension(cameraInfo, ExifDirectoryBase.TagImageWidth, trackHeader, QuickTimeTrackHeaderDirectory.TagWidth),
            Height: GetDimension(cameraInfo, ExifDirectoryBase.TagImageHeight, trackHeader, QuickTimeTrackHeaderDirectory.TagHeight),
            OrientationDegrees: GetOrientationDegrees(cameraInfo),
            DateCaptured: GetDateCaptured(captureDetails),
            Latitude: latitude,
            Longitude: longitude,
            CameraMake: cameraInfo?.GetString(ExifDirectoryBase.TagMake),
            CameraModel: cameraInfo?.GetString(ExifDirectoryBase.TagModel),
            LensModel: captureDetails?.GetString(ExifDirectoryBase.TagLensModel),
            DurationInSeconds: GetDurationInSeconds(movieHeader));
    }

    private (double? Latitude, double? Longitude) GetCoordinates(GpsDirectory? gps)
    {
        if (gps is null)
            return (null, null);
        try
        {
            var location = gps.GetGeoLocation();
            return location is null ? (null, null) : (location.Latitude, location.Longitude);
        }
        catch (Exception ex)
        {
            Log.GpsCoordinatesParseFailed(logger, ex);
            return (null, null);
        }
    }

    // Photos embed dimensions in EXIF IFD0; videos only have QuickTime track headers.
    // Trying EXIF first covers both formats with a single helper.
    private static int GetDimension(
        MetadataExtractor.Directory? exifDirectory, int exifTag,
        QuickTimeTrackHeaderDirectory? trackHeader, int quickTimeTag)
    {
        // Each check below: segment present AND tag readable AND value is a valid positive dimension.
        if (exifDirectory is not null && exifDirectory.TryGetInt32(exifTag, out var exifPixels) && exifPixels > 0)
            return exifPixels;
        if (trackHeader is not null && trackHeader.TryGetInt32(quickTimeTag, out var qtPixels) && qtPixels > 0)
            return qtPixels;
        return 0;
    }

    private static int GetOrientationDegrees(ExifIfd0Directory? cameraInfo)
    {
        // Segment absent OR tag unreadable — assume upright (0°).
        if (cameraInfo is null || !cameraInfo.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation))
            return 0;
        // Raw EXIF orientation tag values per the EXIF 2.3 spec.
        // Only rotations (no mirroring) are represented; mirrored variants map to 0.
        return orientation switch
        {
            6 => 90,
            3 => 180,
            8 => 270,
            _ => 0,
        };
    }

    private LocalDateTime? GetDateCaptured(ExifSubIfdDirectory? captureDetails)
    {
        var raw = captureDetails?.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        if (raw is null)
            return null;
        var result = exifDatePattern.Parse(raw);
        if (!result.Success)
            Log.DateParseFailed(logger, raw);
        return result.Success ? result.Value : null;
    }

    private static int? GetDurationInSeconds(QuickTimeMovieHeaderDirectory? movieHeader)
    {
        if (movieHeader is null)
            return null;
        if (!movieHeader.TryGetInt64(QuickTimeMovieHeaderDirectory.TagDuration, out var duration) ||    // tag absent or unreadable
            !movieHeader.TryGetInt32(QuickTimeMovieHeaderDirectory.TagTimeScale, out var timeScale) ||  // tag absent or unreadable
            timeScale <= 0 ||                                                                           // guard: prevents division by zero
            duration < 0)                                                                               // guard: malformed negative duration
        {
            return null;
        }

        return (int)Math.Round((double)duration / timeScale);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract EXIF metadata from {Path}.")]
        public static partial void ExtractionFailed(ILogger logger, string path, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse GPS coordinates from EXIF data.")]
        public static partial void GpsCoordinatesParseFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse EXIF date '{Raw}'.")]
        public static partial void DateParseFailed(ILogger logger, string raw);
    }
}
