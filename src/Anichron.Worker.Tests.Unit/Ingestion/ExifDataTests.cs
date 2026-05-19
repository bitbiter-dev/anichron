using Anichron.Worker.Ingestion;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class ExifDataTests
{
    [Fact]
    public void Empty_HasAllZeroAndNullFields()
    {
        ExifData.Empty.Width.Should().Be(0);
        ExifData.Empty.Height.Should().Be(0);
        ExifData.Empty.OrientationDegrees.Should().Be(0);
        ExifData.Empty.DateCaptured.Should().BeNull();
        ExifData.Empty.Latitude.Should().BeNull();
        ExifData.Empty.Longitude.Should().BeNull();
        ExifData.Empty.CameraMake.Should().BeNull();
        ExifData.Empty.CameraModel.Should().BeNull();
        ExifData.Empty.LensModel.Should().BeNull();
        ExifData.Empty.DurationInSeconds.Should().BeNull();
    }
}
