using Anichron.Worker.Ingestion.Pipeline;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ExifExtractionMiddleware(
    IFileSystem fileSystem,
    ILogger<ExifExtractionMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.ExifExtraction;
    public bool CanInvoke(IngestionContext context) => true;

    public Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        context.Exif = ExtractExif(context.Item.AbsolutePath);
        return next(context, ct);
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

    internal ExifData BuildExifData(IReadOnlyList<MetadataExtractor.Directory> metadataDirectories)
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
            Orientation: GetOrientation(cameraInfo),
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
            // MetadataExtractor 2.9 turned GeoLocation from a class into a readonly struct, so
            // GetGeoLocation() now returns Nullable<GeoLocation>. `is null` no longer narrows the
            // value for member access — hence the positional pattern, which unwraps it.
            var location = gps.GetGeoLocation();
            return location is { } coordinates
                ? (coordinates.Latitude, coordinates.Longitude)
                : (null, null);
        }
        // Stryker disable once Block
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
        if (exifDirectory is not null && exifDirectory.TryGetInt32(exifTag, out var exifPixels) && exifPixels > 0)
            return exifPixels;
        return trackHeader is not null && trackHeader.TryGetInt32(quickTimeTag, out var qtPixels)
            ? Math.Max(qtPixels, 0)
            : 0;
    }

    private static int GetOrientation(ExifIfd0Directory? cameraInfo)
        => cameraInfo is not null
           && cameraInfo.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation)
           && orientation is
               // Stryker disable once Equality
               >= Core.Domain.ExifOrientation.Upright
               and <= Core.Domain.ExifOrientation.Max
            ? orientation
            : Core.Domain.ExifOrientation.Upright;

    private LocalDateTime? GetDateCaptured(ExifSubIfdDirectory? captureDetails)
    {
        var raw = captureDetails?.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        if (raw is null)
            return null;
        if (ExifDate.Parse(raw) is { } date)
            return date;

        Log.DateParseFailed(logger, raw);
        return null;
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
