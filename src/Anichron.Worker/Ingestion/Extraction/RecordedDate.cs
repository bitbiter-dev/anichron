using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>A capture date as a file records it: a clock time, with the offset the file records beside it.</summary>
/// <param name="At">The recorded clock time.</param>
/// <param name="Offset">The recorded offset, if any.</param>
internal sealed record RecordedDate(LocalDateTime At, Offset? Offset)
{
    /// <summary>The capture date, with the time zone at the asset's position; the zone never changes the recorded values.</summary>
    /// <param name="atPosition">The zone at the asset's position, if it has one.</param>
    public CaptureDate In(DateTimeZone? atPosition) => new(At, Offset, CaptureSource.Recorded, atPosition?.Id);
}
