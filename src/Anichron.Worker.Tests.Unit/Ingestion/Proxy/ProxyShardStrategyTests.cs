using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ProxyShardStrategyTests
{
    // ==========================================================================
    // TwoLevelHexShardStrategy
    // ==========================================================================

    [Fact]
    public void GetDirectory_KnownConfigAndHash_ProducesLiteralTwoLevelPath()
    {
        var configId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");

        var result = new TwoLevelHexShardStrategy().GetDirectory(configId, "0123456789abcdef");

        result.Should().Be("01/abcdef1234567890abcdef1234567890-0123456789abcdef");
    }

    [Fact]
    public void GetDirectory_DifferentHashSameConfig_BucketFollowsTheContentHash()
    {
        var configId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");

        var result = new TwoLevelHexShardStrategy().GetDirectory(configId, "fedcba9876543210");

        result.Should().Be("fe/abcdef1234567890abcdef1234567890-fedcba9876543210");
    }

    [Fact]
    public void GetDirectory_SameConfigAndHash_IsStableAcrossCalls()
    {
        var strategy = new TwoLevelHexShardStrategy();
        var configId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var first = strategy.GetDirectory(configId, "0123456789abcdef");
        var second = strategy.GetDirectory(configId, "0123456789abcdef");

        first.Should().Be("01/33333333333333333333333333333333-0123456789abcdef");
        second.Should().Be("01/33333333333333333333333333333333-0123456789abcdef");
    }

    [Fact]
    public void GetDirectory_DifferentConfigsSameHash_ProduceDifferentDirectories()
    {
        var strategy = new TwoLevelHexShardStrategy();

        var first = strategy.GetDirectory(Guid.Parse("11111111-1111-1111-1111-111111111111"), "0123456789abcdef");
        var second = strategy.GetDirectory(Guid.Parse("22222222-2222-2222-2222-222222222222"), "0123456789abcdef");

        first.Should().Be("01/11111111111111111111111111111111-0123456789abcdef");
        second.Should().Be("01/22222222222222222222222222222222-0123456789abcdef");
    }

    [Theory]
    [InlineData("01/abcdef1234567890abcdef1234567890-0123456789abcdef/thumbnail.jpg")]
    [InlineData("01/abcdef1234567890abcdef1234567890-0123456789abcdef/preview.jpg.0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f.tmp")]
    [InlineData("01/abcdef1234567890abcdef1234567890-0123456789abcdef")]
    public void StorageConfigOf_PathInsideAContentDirectory_IsTheConfigItNames(string relativePath)
        => new TwoLevelHexShardStrategy().StorageConfigOf(relativePath)
            .Should().Be(Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890"));

    [Fact]
    public void StorageConfigOf_IsTheInverseOfGetDirectory()
    {
        var strategy = new TwoLevelHexShardStrategy();
        var configId = Guid.Parse("33333333-4444-5555-6666-777777777777");

        strategy.StorageConfigOf(strategy.GetDirectory(configId, "fedcba9876543210") + "/video.mp4").Should().Be(configId);
    }

    [Theory]
    [InlineData("stray.txt")]
    [InlineData("01/stray.txt")]
    [InlineData("01/abcdef1234567890abcdef1234567890")]
    [InlineData("01/abcdef1234567890abcdef123456789-0123456789abcdef/thumbnail.jpg")]
    [InlineData("01/abcdef12-3456-7890-abcd-ef1234567890-0123456789abcdef/thumbnail.jpg")]
    [InlineData("01/not-a-config/thumbnail.jpg")]
    [InlineData("01/-0123456789abcdef/thumbnail.jpg")]
    public void StorageConfigOf_PathNamingNoConfig_IsNull(string relativePath)
        => new TwoLevelHexShardStrategy().StorageConfigOf(relativePath).Should().BeNull();
}
