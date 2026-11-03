using Anichron.Worker.Ingestion.Pipeline;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.WebP;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class ExifExtractionMiddleware(
    IFileSystem fileSystem,
    ILogger<ExifExtractionMiddleware> logger) : IIngestionMiddleware
{
    private const string heicPrimaryItemProperties = "HEIC Primary Item Properties";

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
        var heicPrimary = metadataDirectories.OfType<HeicImagePropertiesDirectory>()
            .FirstOrDefault(d => d.Name == heicPrimaryItemProperties);                      // HEIC: size, irot
        var videoTrack = metadataDirectories.OfType<QuickTimeTrackHeaderDirectory>()
            .FirstOrDefault(HasSize);                                                       // video: size, rotation
        var movieHeader = metadataDirectories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault(); // video duration

        var (latitude, longitude) = GetCoordinates(gps);
        var (width, height) = GetSize(
            (heicPrimary, HeicImagePropertiesDirectory.TagImageWidth, HeicImagePropertiesDirectory.TagImageHeight),
            (metadataDirectories.OfType<JpegDirectory>().FirstOrDefault(), JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
            (metadataDirectories.OfType<PngDirectory>().FirstOrDefault(), PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
            (metadataDirectories.OfType<GifHeaderDirectory>().FirstOrDefault(), GifHeaderDirectory.TagImageWidth, GifHeaderDirectory.TagImageHeight),
            (metadataDirectories.OfType<WebPDirectory>().FirstOrDefault(), WebPDirectory.TagImageWidth, WebPDirectory.TagImageHeight),
            (metadataDirectories.OfType<BmpHeaderDirectory>().FirstOrDefault(), BmpHeaderDirectory.TagImageWidth, BmpHeaderDirectory.TagImageHeight),
            (cameraInfo, ExifDirectoryBase.TagImageWidth, ExifDirectoryBase.TagImageHeight),
            (videoTrack, QuickTimeTrackHeaderDirectory.TagWidth, QuickTimeTrackHeaderDirectory.TagHeight));

        return new ExifData(
            Width: width,
            Height: height,
            Orientation: GetOrientation(heicPrimary, videoTrack, cameraInfo),
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

    private static bool HasSize(QuickTimeTrackHeaderDirectory track)
        => GetSize((track, QuickTimeTrackHeaderDirectory.TagWidth, QuickTimeTrackHeaderDirectory.TagHeight)) != (0, 0);

    private static (int Width, int Height) GetSize(
        params (MetadataExtractor.Directory? Directory, int WidthTag, int HeightTag)[] sources)
    {
        foreach (var (directory, widthTag, heightTag) in sources)
        {
            if (directory is not null
                && directory.TryGetInt32(widthTag, out var width) && width > 0
                && directory.TryGetInt32(heightTag, out var height) && height > 0)
            {
                return (width, height);
            }
        }

        return (0, 0);
    }

    private static int GetOrientation(
        HeicImagePropertiesDirectory? heicPrimary, QuickTimeTrackHeaderDirectory? videoTrack, ExifIfd0Directory? cameraInfo)
    {
        if (heicPrimary is not null)
            return OrientationOfRotation(heicPrimary.TryGetDouble(HeicImagePropertiesDirectory.TagRotation, out var irot) ? irot : 0);
        if (videoTrack is not null)
            return OrientationOfRotation(videoTrack.TryGetDouble(QuickTimeTrackHeaderDirectory.TagRotation, out var rotation) ? rotation : 0);
        return cameraInfo is not null
           && cameraInfo.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation)
           && orientation is
               // Stryker disable once Equality
               >= Core.Domain.ExifOrientation.Upright
               and <= Core.Domain.ExifOrientation.Max
            ? orientation
            : Core.Domain.ExifOrientation.Upright;
    }

    private static int OrientationOfRotation(double degrees)
        => ((((int)Math.Round(degrees) % 360) + 360) % 360) switch
        {
            90 => 8,
            180 => 3,
            270 => 6,
            _ => Core.Domain.ExifOrientation.Upright,
        };

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
        => movieHeader?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration && duration >= TimeSpan.Zero
            ? (int)Math.Round(duration.TotalSeconds)
            : null;

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
