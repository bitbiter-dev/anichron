using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Extraction;

public sealed class RecordedDateTests
{
    private static LocalDateTime At => new(2026, 10, 30, 19, 9, 51);

    [Fact]
    public void In_ZoneAtThePosition_KeepsTheRecordedTimeAndOffsetBesideIt()
        => new RecordedDate(At, Offset.FromHours(13)).In(DateTimeZoneProviders.Tzdb["Pacific/Auckland"])
            .Should().Be(new CaptureDate(At, Offset.FromHours(13), CaptureSource.Recorded, "Pacific/Auckland"));

    [Fact]
    public void In_ZoneThatDisagreesWithTheOffset_LeavesTheRecordedValuesAlone()
        => new RecordedDate(At, Offset.FromHours(1)).In(DateTimeZoneProviders.Tzdb["Pacific/Auckland"])
            .Should().Be(new CaptureDate(At, Offset.FromHours(1), CaptureSource.Recorded, "Pacific/Auckland"));

    [Fact]
    public void In_NoZone_HasNoZone()
        => new RecordedDate(At, null).In(null).Should().Be(new CaptureDate(At, null, CaptureSource.Recorded, null));
}
