using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Extraction;

public sealed class RecordedDateTests
{
    private static LocalDateTime At => new(2026, 10, 30, 19, 9, 51);

    [Fact]
    public void In_ZoneAtThePosition_KeepsTheRecordedTimeAndOffsetBesideIt()
        => new RecordedDate.ClockTime(At, Offset.FromHours(13)).In(DateTimeZoneProviders.Tzdb["Pacific/Auckland"])
            .Should().Be(new CaptureDate(At, Offset.FromHours(13), CaptureSource.Recorded, "Pacific/Auckland"));

    [Fact]
    public void In_ZoneThatDisagreesWithTheOffset_LeavesTheRecordedValuesAlone()
        => new RecordedDate.ClockTime(At, Offset.FromHours(1)).In(DateTimeZoneProviders.Tzdb["Pacific/Auckland"])
            .Should().Be(new CaptureDate(At, Offset.FromHours(1), CaptureSource.Recorded, "Pacific/Auckland"));

    [Fact]
    public void In_NoZone_HasNoZone()
        => new RecordedDate.ClockTime(At, null).In(null).Should().Be(new CaptureDate(At, null, CaptureSource.Recorded, null));

    [Fact]
    public void In_DateAlone_IsThatDayAtMidnightWithNoOffsetAndDateOnly()
        => new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)).In(DateTimeZoneProviders.Tzdb["Pacific/Auckland"])
            .Should().Be(new CaptureDate(new LocalDateTime(2019, 5, 4, 0, 0), null, CaptureSource.RecordedDateOnly, "Pacific/Auckland"));

    [Fact]
    public void In_DateAloneWithNoZone_HasNoZone()
        => new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)).In(null)
            .Should().Be(new CaptureDate(new LocalDateTime(2019, 5, 4, 0, 0), null, CaptureSource.RecordedDateOnly, null));
}
