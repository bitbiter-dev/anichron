using Anichron.Worker.Ingestion.Proxy;
using SixLabors.ImageSharp;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ProxyBoundTests
{
    [Theory]
    [InlineData(ProxyEdge.LongEdge, 1920, 4000, 3000, 1920, 1440)]
    [InlineData(ProxyEdge.LongEdge, 1920, 3000, 4000, 1440, 1920)]
    [InlineData(ProxyEdge.LongEdge, 1920, 6016, 6016, 1920, 1920)]
    [InlineData(ProxyEdge.ShortEdge, 300, 4000, 3000, 400, 300)]
    [InlineData(ProxyEdge.ShortEdge, 300, 3000, 4000, 300, 400)]
    [InlineData(ProxyEdge.LongEdge, 64, 1000, 333, 64, 21)]
    [InlineData(ProxyEdge.LongEdge, 10, 10000, 1, 10, 1)]
    public void FitTo_LargerSource_ShrinksBoundedEdgeToPixelsKeepingAspect(
        ProxyEdge edge, int pixels, int sourceWidth, int sourceHeight, int expectedWidth, int expectedHeight)
    {
        new ProxyBound(edge, pixels).FitTo(new Size(sourceWidth, sourceHeight))
            .Should().Be(new Size(expectedWidth, expectedHeight));
    }

    [Theory]
    [InlineData(ProxyEdge.LongEdge, 1920, 1000, 800)]
    [InlineData(ProxyEdge.LongEdge, 1920, 1920, 1080)]
    [InlineData(ProxyEdge.ShortEdge, 300, 200, 100)]
    [InlineData(ProxyEdge.ShortEdge, 300, 900, 300)]
    public void FitTo_SourceWithinBound_ReturnsSourceUnchanged(
        ProxyEdge edge, int pixels, int sourceWidth, int sourceHeight)
    {
        new ProxyBound(edge, pixels).FitTo(new Size(sourceWidth, sourceHeight))
            .Should().Be(new Size(sourceWidth, sourceHeight));
    }
}
