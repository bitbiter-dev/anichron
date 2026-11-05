using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>A capture date as a file records it: a clock time, or a date alone.</summary>
internal abstract record RecordedDate
{
    private RecordedDate()
    {
    }

    /// <summary>The capture date, with the time zone at the asset's position; the zone never changes the recorded values.</summary>
    /// <param name="atPosition">The zone at the asset's position, if it has one.</param>
    public abstract CaptureDate In(DateTimeZone? atPosition);

    /// <summary>A clock time, with the offset the file records beside it.</summary>
    /// <param name="At">The recorded clock time.</param>
    /// <param name="Offset">The recorded offset, if any.</param>
    internal sealed record ClockTime(LocalDateTime At, Offset? Offset) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition) => new(At, Offset, CaptureSource.Recorded, atPosition?.Id);
    }

    /// <summary>A date the file records without a time; it dates the asset at 00:00 and keeps no offset.</summary>
    /// <param name="Date">The recorded date.</param>
    internal sealed record DateAlone(LocalDate Date) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition)
            => new(Date.AtMidnight(), null, CaptureSource.RecordedDateOnly, atPosition?.Id);
    }
}
