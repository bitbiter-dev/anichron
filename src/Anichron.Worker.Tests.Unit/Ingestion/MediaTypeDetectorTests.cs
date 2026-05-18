using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class MediaTypeDetectorTests
{
    // ==========================================================================
    // Image extensions
    // ==========================================================================

    [Theory]
    [InlineData("/photo.jpg")]
    [InlineData("/photo.jpeg")]
    [InlineData("/photo.png")]
    [InlineData("/photo.heic")]
    [InlineData("/photo.heif")]
    [InlineData("/photo.gif")]
    [InlineData("/photo.webp")]
    [InlineData("/photo.tiff")]
    [InlineData("/photo.tif")]
    [InlineData("/photo.bmp")]
    public void Detect_ImageExtension_ReturnsImage(string filePath)
    {
        MediaTypeDetector.Detect(filePath).Should().Be(MediaType.Image);
    }

    // ==========================================================================
    // Video extensions
    // ==========================================================================

    [Theory]
    [InlineData("/video.mov")]
    [InlineData("/video.mp4")]
    [InlineData("/video.m4v")]
    [InlineData("/video.avi")]
    [InlineData("/video.mkv")]
    [InlineData("/video.wmv")]
    public void Detect_VideoExtension_ReturnsVideo(string filePath)
    {
        MediaTypeDetector.Detect(filePath).Should().Be(MediaType.Video);
    }

    // ==========================================================================
    // Case insensitivity
    // ==========================================================================

    [Theory]
    [InlineData("/photo.JPG", MediaType.Image)]
    [InlineData("/photo.HEIC", MediaType.Image)]
    [InlineData("/photo.Jpeg", MediaType.Image)]
    [InlineData("/video.MOV", MediaType.Video)]
    [InlineData("/video.Mp4", MediaType.Video)]
    public void Detect_UppercaseExtension_ReturnsCorrectType(string filePath, MediaType expected)
    {
        MediaTypeDetector.Detect(filePath).Should().Be(expected);
    }

    // ==========================================================================
    // Unknown extension
    // ==========================================================================

    [Theory]
    [InlineData("/document.pdf")]
    [InlineData("/archive.zip")]
    [InlineData("/text.txt")]
    [InlineData("/noextension")]
    public void Detect_UnknownExtension_ReturnsNull(string filePath)
    {
        MediaTypeDetector.Detect(filePath).Should().BeNull();
    }
}
