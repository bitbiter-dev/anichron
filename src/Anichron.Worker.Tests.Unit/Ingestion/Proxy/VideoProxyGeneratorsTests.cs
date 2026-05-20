using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class VideoProxyGeneratorsTests
{
    // ==========================================================================
    // Video720PGenerator
    // ==========================================================================

    [Fact]
    public void Video720PGenerator_FileName_IsVideo720pMp4()
    {
        var generator = new Video720PGenerator(Substitute.For<IVideoProcessor>());

        generator.FileName.Should().Be("video_720p.mp4");
    }

    [Fact]
    public void Video720PGenerator_ProxyType_IsWebVideo()
    {
        var generator = new Video720PGenerator(Substitute.For<IVideoProcessor>());

        generator.ProxyType.Should().Be(ProxyType.WebVideo);
    }

    [Fact]
    public async Task Video720PGenerator_TranscodeAsync_DelegatesToVideoProcessorAsync()
    {
        var processor = Substitute.For<IVideoProcessor>();
        var generator = new Video720PGenerator(processor);

        await generator.TranscodeAsync("/src/video.mp4", "/out/video_720p.mp4", CancellationToken.None);

        await processor.Received(1).TranscodeAsync("/src/video.mp4", "/out/video_720p.mp4", CancellationToken.None);
    }
}
