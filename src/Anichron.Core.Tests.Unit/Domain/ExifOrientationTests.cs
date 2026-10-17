using Anichron.Core.Domain;

namespace Anichron.Core.Tests.Unit.Domain;

public sealed class ExifOrientationTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    public void SwapsAxes_IsTrueOnlyForTheQuarterTurns(int orientation, bool swaps)
        => ExifOrientation.SwapsAxes(orientation).Should().Be(swaps);

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-6)]
    public void SwapsAxes_OutOfRange_IsFalse(int orientation)
        => ExifOrientation.SwapsAxes(orientation).Should().BeFalse();
}
