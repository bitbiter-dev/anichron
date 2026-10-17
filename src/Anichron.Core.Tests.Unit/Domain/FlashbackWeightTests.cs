using Anichron.Core.Domain;

namespace Anichron.Core.Tests.Unit.Domain;

public sealed class FlashbackWeightTests
{
    [Theory]
    [InlineData(true, 2.0)]
    [InlineData(false, 1.0)]
    public void For_LikedWeighsDouble(bool liked, double weight)
        => FlashbackWeight.For(liked).Should().Be(weight);
}
