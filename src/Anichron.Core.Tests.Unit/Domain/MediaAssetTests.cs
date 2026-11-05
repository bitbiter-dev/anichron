using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Domain;

public sealed class MediaAssetTests
{
    private static CaptureDate InAuckland => new(
        new LocalDateTime(2019, 7, 23, 18, 5), Offset.FromHours(12), CaptureSource.Recorded, "Pacific/Auckland");

    [Fact]
    public void Captured_Set_StoresEveryPartInItsColumn()
    {
        var asset = new MediaAsset { Captured = InAuckland };

        asset.Should().BeEquivalentTo(new
        {
            DateCaptured = new LocalDateTime(2019, 7, 23, 18, 5),
            CaptureOffset = Offset.FromHours(12),
            CaptureSource = CaptureSource.Recorded,
            CaptureZone = "Pacific/Auckland",
            Year = 2019,
            Month = 7,
            Day = 23,
        });
    }

    [Fact]
    public void Captured_Get_ComposesTheColumns()
    {
        var asset = new MediaAsset { Captured = InAuckland };

        asset.Captured.Should().Be(InAuckland);
    }

    [Fact]
    public void Captured_WithoutOffsetOrZone_KeepsThemEmpty()
    {
        var recorded = new CaptureDate(new LocalDateTime(2024, 2, 29, 9, 0), null, CaptureSource.Recorded, null);

        var asset = new MediaAsset { Captured = recorded };

        asset.Captured.Should().Be(recorded);
    }

    [Fact]
    public void Captured_SetAgain_ReplacesEveryPart()
    {
        var asset = new MediaAsset { Captured = InAuckland };

        asset.Captured = new CaptureDate(new LocalDateTime(2024, 2, 29, 9, 0), null, CaptureSource.Recorded, null);

        asset.Should().BeEquivalentTo(new
        {
            DateCaptured = new LocalDateTime(2024, 2, 29, 9, 0),
            CaptureOffset = (Offset?)null,
            CaptureZone = (string?)null,
            Year = 2024,
            Month = 2,
            Day = 29,
        });
    }

    [Fact]
    public void Captured_SetToNone_MakesTheAssetUndated()
    {
        var asset = new MediaAsset { Captured = InAuckland };

        asset.Captured = null;

        asset.Should().BeEquivalentTo(new
        {
            Captured = (CaptureDate?)null,
            DateCaptured = (LocalDateTime?)null,
            CaptureOffset = (Offset?)null,
            CaptureSource = (CaptureSource?)null,
            CaptureZone = (string?)null,
            Year = (int?)null,
            Month = (int?)null,
            Day = (int?)null,
        });
    }

    [Fact]
    public void Captured_New_IsUndated()
        => new MediaAsset().Captured.Should().BeNull();
}
