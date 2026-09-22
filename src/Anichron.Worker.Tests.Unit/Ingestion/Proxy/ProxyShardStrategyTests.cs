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
}
