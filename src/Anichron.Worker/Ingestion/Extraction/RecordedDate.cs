using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>A capture date as a file records it: a clock time, a date alone, or a moment recorded only in UTC.</summary>
internal abstract record RecordedDate
{
    private RecordedDate()
    {
    }

    /// <summary>The capture date, with the time zone at the asset's position; the zone never changes a recorded clock time.</summary>
    /// <param name="atPosition">The zone at the asset's position, if it has one.</param>
    /// <param name="library">The library's zone, which reads a moment recorded without a position.</param>
    public abstract CaptureDate In(DateTimeZone? atPosition, DateTimeZone library);

    /// <summary>A clock time, with the offset the file records beside it.</summary>
    /// <param name="At">The recorded clock time.</param>
    /// <param name="Offset">The recorded offset, if any.</param>
    internal sealed record ClockTime(LocalDateTime At, Offset? Offset) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition, DateTimeZone library)
            => new(At, Offset, CaptureSource.Recorded, atPosition?.Id);
    }

    /// <summary>A date the file records without a time; it dates the asset at 00:00 and keeps no offset.</summary>
    /// <param name="Date">The recorded date.</param>
    internal sealed record DateAlone(LocalDate Date) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition, DateTimeZone library)
            => new(Date.AtMidnight(), null, CaptureSource.RecordedDateOnly, atPosition?.Id);
    }

    /// <summary>A moment the file records only in UTC; it keeps no offset, since the zone it is read in gives one.</summary>
    /// <param name="At">The recorded moment.</param>
    internal sealed record Moment(Instant At) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition, DateTimeZone library)
            => new(At.InZone(atPosition ?? library).LocalDateTime, null, CaptureSource.RecordedInUtc, atPosition?.Id);
    }
}
