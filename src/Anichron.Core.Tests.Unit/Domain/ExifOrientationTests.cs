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

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(90.0, 8)]
    [InlineData(89.9999, 8)]
    [InlineData(90.0001, 8)]
    [InlineData(450.0, 8)]
    [InlineData(180.0, 3)]
    [InlineData(-180.0, 3)]
    [InlineData(270.0, 6)]
    [InlineData(-90.0, 6)]
    [InlineData(45.0, 1)]
    public void OfDisplayRotation_MapsAnticlockwiseDegreesToTheEquivalentOrientation(double degrees, int expected)
        => ExifOrientation.OfDisplayRotation(degrees).Should().Be(expected);

    [Fact]
    public void Compose_NoSteps_IsUpright()
        => ExifOrientation.Compose([]).Should().Be(1);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Compose_OneStep_IsThatStep(int orientation)
        => ExifOrientation.Compose([orientation]).Should().Be(orientation);

    // Each expected value follows where the stored top-left corner lands after the steps, read off the EXIF table:
    // 2 flips left-right, 4 top-bottom, 5 transposes, 7 transverses, 6 turns clockwise, 8 anticlockwise.
    [Theory]
    [InlineData(8, 4, 5)]
    [InlineData(4, 8, 7)]
    [InlineData(8, 2, 7)]
    [InlineData(2, 6, 7)]
    [InlineData(6, 2, 5)]
    [InlineData(3, 2, 4)]
    [InlineData(4, 2, 3)]
    [InlineData(2, 2, 1)]
    [InlineData(6, 6, 3)]
    [InlineData(6, 8, 1)]
    [InlineData(8, 8, 3)]
    [InlineData(5, 5, 1)]
    [InlineData(5, 7, 3)]
    [InlineData(7, 6, 4)]
    public void Compose_TwoSteps_IsTheFirstThenTheSecond(int first, int second, int expected)
        => ExifOrientation.Compose([first, second]).Should().Be(expected);

    [Fact]
    public void Compose_ThreeSteps_AppliesThemInOrder()
        => ExifOrientation.Compose([6, 2, 6]).Should().Be(2);
}
