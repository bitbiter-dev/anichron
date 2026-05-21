using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Tests.Unit.Settings;

public sealed class WorkerSettingsValidatorTests
{
    private static readonly WorkerSettings ValidSettings = new()
    {
        MaxConcurrentFiles = 4,
        CrawlIntervalHours = 4,
        ThumbnailMaxWidth = 300,
        ThumbnailJpegQuality = 75,
        PreviewMaxWidth = 1920,
        PreviewJpegQuality = 85,
        BlurhashSampleWidth = 64,
        VideoMaxHeight = 720,
        VideoBitrateKbps = 2000,
    };

    private static ValidateOptionsResult Validate(WorkerSettings settings)
        => new WorkerSettingsValidator().Validate(null, settings);

    // ==========================================================================
    // Valid settings
    // ==========================================================================

    [Fact]
    public void Validate_AllValid_ReturnsSuccess()
    {
        Validate(ValidSettings).Succeeded.Should().BeTrue();
    }

    // ==========================================================================
    // MaxConcurrentFiles
    // ==========================================================================

    [Fact]
    public void Validate_MaxConcurrentFilesZero_ReturnsFailed()
    {
        Validate(ValidSettings with { MaxConcurrentFiles = 0 }).Failed.Should().BeTrue();
    }

    // ==========================================================================
    // JPEG quality bounds
    // ==========================================================================

    [Fact]
    public void Validate_ThumbnailJpegQualityZero_ReturnsFailed()
    {
        Validate(ValidSettings with { ThumbnailJpegQuality = 0 }).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_ThumbnailJpegQuality101_ReturnsFailed()
    {
        Validate(ValidSettings with { ThumbnailJpegQuality = 101 }).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_PreviewJpegQualityZero_ReturnsFailed()
    {
        Validate(ValidSettings with { PreviewJpegQuality = 0 }).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_PreviewJpegQuality101_ReturnsFailed()
    {
        Validate(ValidSettings with { PreviewJpegQuality = 101 }).Failed.Should().BeTrue();
    }

    // ==========================================================================
    // Video settings
    // ==========================================================================

    [Fact]
    public void Validate_VideoMaxHeightZero_ReturnsFailed()
    {
        Validate(ValidSettings with { VideoMaxHeight = 0 }).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_VideoBitrateKbpsZero_ReturnsFailed()
    {
        Validate(ValidSettings with { VideoBitrateKbps = 0 }).Failed.Should().BeTrue();
    }

    // ==========================================================================
    // Multiple failures
    // ==========================================================================

    [Fact]
    public void Validate_MultipleInvalidSettings_ReportsAllFailures()
    {
        var result = Validate(ValidSettings with { ThumbnailJpegQuality = 0, VideoBitrateKbps = -1 });

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2);
    }
}
