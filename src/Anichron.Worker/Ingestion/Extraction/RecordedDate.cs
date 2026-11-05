using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>A capture date as a file records it: a clock time, or a moment recorded only in UTC.</summary>
internal abstract record RecordedDate
{
    private RecordedDate()
    {
    }

    /// <summary>The capture date, with the time zone at the asset's position; the zone never changes a recorded clock time.</summary>
    /// <param name="atPosition">The zone at the asset's position, if it has one.</param>
    /// <param name="library">The library's zone, which reads a moment recorded without a position.</param>
    public abstract CaptureDate In(DateTimeZone? atPosition, DateTimeZone library);

    /// <summary>A recorded clock time, with the offset the file records beside it.</summary>
    /// <param name="At">The recorded clock time.</param>
    /// <param name="Offset">The recorded offset, if any.</param>
    public sealed record ClockTime(LocalDateTime At, Offset? Offset) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition, DateTimeZone library)
            => new(At, Offset, CaptureSource.Recorded, atPosition?.Id);
    }

    /// <summary>A moment the file records only in UTC; it keeps no offset, since the zone it is read in gives one.</summary>
    /// <param name="At">The recorded moment.</param>
    public sealed record Moment(Instant At) : RecordedDate
    {
        public override CaptureDate In(DateTimeZone? atPosition, DateTimeZone library)
            => new(At.InZone(atPosition ?? library).LocalDateTime, null, CaptureSource.RecordedInUtc, atPosition?.Id);
    }
}
