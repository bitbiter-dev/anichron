using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ImageProxyGeneratorsTests
{
    // ==========================================================================
    // TwoLevelHexShardStrategy
    // ==========================================================================

    [Fact]
    public void GetDirectory_ProducesCorrectTwoLevelPath()
    {
        var id = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");

        var result = new TwoLevelHexShardStrategy().GetDirectory(id);

        result.Should().Be("ab/cdef1234567890abcdef1234567890");
    }

    [Fact]
    public void GetDirectory_TopDirectoryIsAlwaysTwoChars()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var top = new TwoLevelHexShardStrategy().GetDirectory(id).Split('/')[0];

        top.Should().HaveLength(2);
    }

    // ==========================================================================
    // BlurhashGenerator
    // ==========================================================================

    [Fact]
    public async Task GenerateAsync_ReturnsUtf8EncodedHashBytesAsync()
    {
        var processor = Substitute.For<IImageProcessor>();
        processor.ComputeBlurhashAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns("HASH");

        var bytes = await new BlurhashGenerator(processor).GenerateAsync(Stream.Null, CancellationToken.None);

        bytes.Should().Equal("HASH"u8.ToArray());
    }
}
