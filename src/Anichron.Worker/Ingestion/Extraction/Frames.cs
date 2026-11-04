using Anichron.Core.Domain;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.WebP;
using Directory = MetadataExtractor.Directory;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>Reads a file's stored size and the orientation it is shown in.</summary>
internal static class Frames
{
    private const string heicPrimaryItemProperties = "HEIC Primary Item Properties";

    public static (int Width, int Height, int Orientation) Of(IReadOnlyList<Directory> directories)
    {
        var cameraInfo = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var heicPrimary = directories.OfType<HeicImagePropertiesDirectory>()
            .FirstOrDefault(d => d.Name == heicPrimaryItemProperties);
        var videoTrack = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault(HasSize);

        var (width, height) = GetSize(
            new SizeSource(heicPrimary, HeicImagePropertiesDirectory.TagImageWidth, HeicImagePropertiesDirectory.TagImageHeight),
            SizeSource.First<JpegDirectory>(directories, JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
            SizeSource.First<PngDirectory>(directories, PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
            SizeSource.First<GifHeaderDirectory>(directories, GifHeaderDirectory.TagImageWidth, GifHeaderDirectory.TagImageHeight),
            SizeSource.First<WebPDirectory>(directories, WebPDirectory.TagImageWidth, WebPDirectory.TagImageHeight),
            SizeSource.First<BmpHeaderDirectory>(directories, BmpHeaderDirectory.TagImageWidth, BmpHeaderDirectory.TagImageHeight),
            new SizeSource(cameraInfo, ExifDirectoryBase.TagImageWidth, ExifDirectoryBase.TagImageHeight),
            new SizeSource(videoTrack, QuickTimeTrackHeaderDirectory.TagWidth, QuickTimeTrackHeaderDirectory.TagHeight));

        return (width, height, GetOrientation(heicPrimary, videoTrack, cameraInfo));
    }

    private static bool HasSize(QuickTimeTrackHeaderDirectory track)
        => new SizeSource(track, QuickTimeTrackHeaderDirectory.TagWidth, QuickTimeTrackHeaderDirectory.TagHeight).Read() is not null;

    private static (int Width, int Height) GetSize(params SizeSource[] sources)
        => sources.Select(source => source.Read()).FirstOrDefault(size => size is not null) ?? (0, 0);

    private static int GetOrientation(
        HeicImagePropertiesDirectory? heicPrimary, QuickTimeTrackHeaderDirectory? videoTrack, ExifIfd0Directory? cameraInfo)
    {
        if (heicPrimary is not null)
            return ExifOrientation.OfDisplayRotation(RotationOf(heicPrimary, HeicImagePropertiesDirectory.TagRotation));
        if (videoTrack is not null)
            return ExifOrientation.OfDisplayRotation(RotationOf(videoTrack, QuickTimeTrackHeaderDirectory.TagRotation));
        return cameraInfo is not null
           && cameraInfo.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation)
           && orientation is
               // Stryker disable once Equality
               >= ExifOrientation.Upright
               and <= ExifOrientation.Max
            ? orientation
            : ExifOrientation.Upright;
    }

    private static double RotationOf(Directory directory, int rotationTag)
        => directory.TryGetDouble(rotationTag, out var degrees) ? degrees : 0;

    private readonly record struct SizeSource(Directory? Directory, int WidthTag, int HeightTag)
    {
        public static SizeSource First<T>(IEnumerable<Directory> directories, int widthTag, int heightTag)
            where T : Directory
            => new(directories.OfType<T>().FirstOrDefault(), widthTag, heightTag);

        public (int Width, int Height)? Read()
            => Directory is not null
               && Directory.TryGetInt32(WidthTag, out var width) && width > 0
               && Directory.TryGetInt32(HeightTag, out var height) && height > 0
                ? (width, height)
                : null;
    }
}
