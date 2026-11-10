using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class VideoProxyGeneratorsTests
{
    // ==========================================================================
    // WebVideoGenerator
    // ==========================================================================

    [Fact]
    public void WebVideoGenerator_FileName_IsVideoMp4()
    {
        var generator = new WebVideoGenerator(Substitute.For<IVideoProcessor>());

        generator.FileName.Should().Be("video.mp4");
    }

    [Fact]
    public void WebVideoGenerator_ProxyType_IsWebVideo()
    {
        var generator = new WebVideoGenerator(Substitute.For<IVideoProcessor>());

        generator.ProxyType.Should().Be(ProxyType.WebVideo);
    }

    [Fact]
    public async Task WebVideoGenerator_TranscodeAsync_DelegatesToVideoProcessorAsync()
    {
        var processor = Substitute.For<IVideoProcessor>();
        var generator = new WebVideoGenerator(processor);

        await generator.TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        await processor.Received(1).TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);
    }
}
