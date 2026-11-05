using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using Anichron.Worker.Ingestion.Pipeline;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class MetadataExtractionMiddleware(
    IFileSystem fileSystem,
    ILogger<MetadataExtractionMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.MetadataExtraction;
    public bool CanInvoke(IngestionContext context) => true;

    public Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        context.Extracted = ExtractFrom(context.Item.AbsolutePath, context.Item.PrimaryMediaType);
        return next(context, ct);
    }

    private ExtractedMedia ExtractFrom(string absolutePath, MediaType mediaType)
    {
        try
        {
            using var stream = fileSystem.File.OpenRead(absolutePath);
            return Extract(ImageMetadataReader.ReadMetadata(stream), mediaType);
        }
        catch (Exception ex)
        {
            Log.ExtractionFailed(logger, absolutePath, ex);
            return ExtractedMedia.Empty;
        }
    }

    internal ExtractedMedia Extract(IReadOnlyList<MetadataExtractor.Directory> directories, MediaType mediaType)
    {
        var cameraInfo = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var captureDetails = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var (width, height, orientation) = Frames.Of(directories);
        var (latitude, longitude) = Positions.Of(directories, logger);

        return new ExtractedMedia(
            RecordedDates.Of(directories, mediaType, logger),
            new MediaDescription(
                width,
                height,
                orientation,
                latitude,
                longitude,
                cameraInfo?.GetString(ExifDirectoryBase.TagMake),
                cameraInfo?.GetString(ExifDirectoryBase.TagModel),
                captureDetails?.GetString(ExifDirectoryBase.TagLensModel),
                Durations.Of(directories)));
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract EXIF metadata from {Path}.")]
        public static partial void ExtractionFailed(ILogger logger, string path, Exception exception);
    }
}
