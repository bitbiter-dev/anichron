using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Domain;

public sealed class MediaAssetTests
{
    [Fact]
    public void DateCaptured_Set_SetsTheFlashbackDateParts()
    {
        var asset = new MediaAsset { DateCaptured = new LocalDateTime(2019, 7, 23, 18, 5) };

        asset.Should().BeEquivalentTo(new { Year = 2019, Month = 7, Day = 23 });
    }

    [Fact]
    public void DateCaptured_SetAgain_ReplacesTheFlashbackDateParts()
    {
        var asset = new MediaAsset { DateCaptured = new LocalDateTime(2019, 7, 23, 18, 5) };

        asset.DateCaptured = new LocalDateTime(2024, 2, 29, 9, 0);

        asset.Should().BeEquivalentTo(new { Year = 2024, Month = 2, Day = 29 });
    }
}
