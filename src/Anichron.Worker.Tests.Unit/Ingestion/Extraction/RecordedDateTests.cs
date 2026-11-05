using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Extraction;

public sealed class RecordedDateTests
{
    private static LocalDateTime At => new(2026, 10, 30, 19, 9, 51);

    private static DateTimeZone Auckland => DateTimeZoneProviders.Tzdb["Pacific/Auckland"];

    private static DateTimeZone Berlin => DateTimeZoneProviders.Tzdb["Europe/Berlin"];

    [Fact]
    public void In_ZoneAtThePosition_KeepsTheRecordedTimeAndOffsetBesideIt()
        => new RecordedDate.ClockTime(At, Offset.FromHours(13)).In(Auckland, Berlin)
            .Should().Be(new CaptureDate(At, Offset.FromHours(13), CaptureSource.Recorded, "Pacific/Auckland"));

    [Fact]
    public void In_ZoneThatDisagreesWithTheOffset_LeavesTheRecordedValuesAlone()
        => new RecordedDate.ClockTime(At, Offset.FromHours(1)).In(Auckland, Berlin)
            .Should().Be(new CaptureDate(At, Offset.FromHours(1), CaptureSource.Recorded, "Pacific/Auckland"));

    [Fact]
    public void In_NoZone_HasNoZone()
        => new RecordedDate.ClockTime(At, null).In(null, Berlin).Should().Be(new CaptureDate(At, null, CaptureSource.Recorded, null));

    [Fact]
    public void In_MomentWithAZoneAtThePosition_IsReadInThatZone()
        => new RecordedDate.Moment(Instant.FromUtc(2026, 10, 30, 6, 10, 45)).In(Auckland, Berlin)
            .Should().Be(new CaptureDate(new LocalDateTime(2026, 10, 30, 19, 10, 45), null, CaptureSource.RecordedInUtc, "Pacific/Auckland"));

    [Fact]
    public void In_MomentWithoutAPosition_IsReadInTheLibraryZoneAndHasNoZone()
        => new RecordedDate.Moment(Instant.FromUtc(2026, 10, 29, 23, 30)).In(null, Berlin)
            .Should().Be(new CaptureDate(new LocalDateTime(2026, 10, 30, 0, 30), null, CaptureSource.RecordedInUtc, null));

    [Fact]
    public void In_DateAlone_IsThatDayAtMidnightWithNoOffsetAndDateOnly()
        => new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)).In(Auckland, Berlin)
            .Should().Be(new CaptureDate(new LocalDateTime(2019, 5, 4, 0, 0), null, CaptureSource.RecordedDateOnly, "Pacific/Auckland"));

    [Fact]
    public void In_DateAloneWithNoZone_HasNoZone()
        => new RecordedDate.DateAlone(new LocalDate(2019, 5, 4)).In(null, Berlin)
            .Should().Be(new CaptureDate(new LocalDateTime(2019, 5, 4, 0, 0), null, CaptureSource.RecordedDateOnly, null));
}
