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
            var directories = ImageMetadataReader.ReadMetadata(stream);
            return BuildExifData(directories);
        }
        catch (Exception ex)
        {
            Log.ExtractionFailed(logger, absolutePath, ex);
            return new ExifData(0, 0, 0, null, null, null, null, null, null, null);
        }
    }

    private static ExifData BuildExifData(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var trackHeader = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault();
        var movieHeader = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();

        var (latitude, longitude) = GetCoordinates(gps);

        return new ExifData(
            Width: GetWidth(ifd0, trackHeader),
            Height: GetHeight(ifd0, trackHeader),
            OrientationDegrees: GetOrientationDegrees(ifd0),
            DateCaptured: GetDateCaptured(subIfd),
            Latitude: latitude,
            Longitude: longitude,
            CameraMake: ifd0?.GetString(ExifDirectoryBase.TagMake),
            CameraModel: ifd0?.GetString(ExifDirectoryBase.TagModel),
            LensModel: subIfd?.GetString(ExifDirectoryBase.TagLensModel),
            DurationInSeconds: GetDurationInSeconds(movieHeader));
    }

    private static int GetWidth(ExifIfd0Directory? ifd0, QuickTimeTrackHeaderDirectory? trackHeader)
    {
        if (ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var w) && w > 0)
            return w;
        if (trackHeader is not null && trackHeader.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out var qtW) && qtW > 0)
            return qtW;
        return 0;
    }

    private static int GetHeight(ExifIfd0Directory? ifd0, QuickTimeTrackHeaderDirectory? trackHeader)
    {
        if (ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var h) && h > 0)
            return h;
        if (trackHeader is not null && trackHeader.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out var qtH) && qtH > 0)
            return qtH;
        return 0;
    }

    private static int GetOrientationDegrees(ExifIfd0Directory? ifd0)
    {
        if (ifd0 is null || !ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation))
            return 0;
        return orientation switch
        {
            1 => 0,
            6 => 90,
            3 => 180,
            8 => 270,
            _ => 0,
        };
    }

    private static LocalDateTime? GetDateCaptured(ExifSubIfdDirectory? subIfd)
    {
        var raw = subIfd?.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        if (raw is null)
            return null;
        var result = exifDatePattern.Parse(raw);
        return result.Success ? result.Value : null;
    }

    private static (double? Latitude, double? Longitude) GetCoordinates(GpsDirectory? gps)
    {
        if (gps is null)
            return (null, null);
        try
        {
            var location = gps.GetGeoLocation();
            return location is null ? (null, null) : (location.Latitude, location.Longitude);
        }
        catch
        {
            return (null, null);
        }
    }

    private static int? GetDurationInSeconds(QuickTimeMovieHeaderDirectory? movieHeader)
    {
        if (movieHeader is null)
            return null;
        if (!movieHeader.TryGetInt64(QuickTimeMovieHeaderDirectory.TagDuration, out var duration) ||
            !movieHeader.TryGetInt32(QuickTimeMovieHeaderDirectory.TagTimeScale, out var timeScale) ||
            timeScale <= 0)
        {
            return null;
        }

        return (int)Math.Round((double)duration / timeScale);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract EXIF metadata from {Path}.")]
        public static partial void ExtractionFailed(ILogger logger, string path, Exception exception);
    }
}
