using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Testing.Domain;

public static class Recorded
{
    /// <summary>A capture date the file records as a clock time alone, with no offset and no position.</summary>
    /// <param name="at">The recorded clock time.</param>
    public static CaptureDate At(LocalDateTime at) => new(at, null, CaptureSource.Recorded, null);
}
