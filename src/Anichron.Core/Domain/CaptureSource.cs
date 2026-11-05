namespace Anichron.Core.Domain;

public enum CaptureSource
{
    /// <summary>A clock time the file records, with or without an offset.</summary>
    Recorded = 1,

    /// <summary>A date the file records without a time, stored at 00:00; its asset is never a burst candidate.</summary>
    RecordedDateOnly = 2,
}
