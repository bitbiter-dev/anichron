namespace Anichron.Core.Domain;

public enum CaptureSource
{
    /// <summary>A clock time the file records, with or without an offset.</summary>
    Recorded = 1,

    /// <summary>A date the file records without a time, stored at 00:00.</summary>
    RecordedDateOnly = 2,

    /// <summary>A moment the file records only in UTC, read in the zone where it was taken, else in the library's zone.</summary>
    RecordedInUtc = 3,
}
