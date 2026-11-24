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
            var directories = ImageMetadataReader.ReadMetadata(stream);
            return Extract(
                directories,
                mediaType == MediaType.Video ? AppleCreationDateOf(stream, absolutePath) : null,
                Frames.IsHeif(directories) ? HeifTransformsOf(stream) : [],
                mediaType);
        }
        catch (Exception ex) when (BrokenFile.Rejected(ex))
        {
            Log.BrokenFileUnreadable(logger, absolutePath, ex.Message);
            return ExtractedMedia.Empty;
        }
        catch (Exception ex)
        {
            Log.ExtractionFailed(logger, absolutePath, ex);
            return ExtractedMedia.Empty;
        }
    }

    private string? AppleCreationDateOf(Stream stream, string absolutePath)
    {
        try
        {
            stream.Position = 0;
            return AppleCreationDates.Read(stream);
        }
        catch (Exception ex)
        {
            Log.AppleCreationDateUnreadable(logger, absolutePath, ex);
            return null;
        }
    }

    private static IReadOnlyList<int> HeifTransformsOf(Stream stream)
    {
        stream.Position = 0;
        return HeifTransforms.Read(stream);
    }

    internal ExtractedMedia Extract(
        IReadOnlyList<MetadataExtractor.Directory> directories, string? appleCreationDate, IReadOnlyList<int> heifTransforms, MediaType mediaType)
    {
        var cameraInfo = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var captureDetails = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var (width, height, orientation) = Frames.Of(directories, heifTransforms);
        var (latitude, longitude) = Positions.Of(directories, logger);

        return new ExtractedMedia(
            RecordedDates.Of(directories, appleCreationDate, mediaType, logger),
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
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract metadata from {Path}.")]
        public static partial void ExtractionFailed(ILogger logger, string path, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to extract metadata from {Path}: {Reason}")]
        public static partial void BrokenFileUnreadable(ILogger logger, string path, string reason);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to read the Apple creation date of {Path}; the video is dated by its other sources.")]
        public static partial void AppleCreationDateUnreadable(ILogger logger, string path, Exception exception);
    }
}
