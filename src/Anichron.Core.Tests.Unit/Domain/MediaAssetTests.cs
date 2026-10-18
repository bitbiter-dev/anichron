using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Domain;

public sealed class MediaAssetTests
{
    [Fact]
    public void SetCaptureDate_SetsTheDateAndItsFlashbackParts()
    {
        var asset = new MediaAsset();

        asset.SetCaptureDate(new LocalDateTime(2021, 11, 7, 18, 30));

        asset.DateCaptured.Should().Be(new LocalDateTime(2021, 11, 7, 18, 30));
        (asset.Year, asset.Month, asset.Day).Should().Be((2021, 11, 7));
    }

    [Fact]
    public void SetCaptureDate_CalledAgain_ReplacesEveryPart()
    {
        var asset = new MediaAsset();
        asset.SetCaptureDate(new LocalDateTime(2021, 11, 7, 18, 30));

        asset.SetCaptureDate(new LocalDateTime(2024, 2, 29, 6, 0));

        asset.DateCaptured.Should().Be(new LocalDateTime(2024, 2, 29, 6, 0));
        (asset.Year, asset.Month, asset.Day).Should().Be((2024, 2, 29));
    }
}
