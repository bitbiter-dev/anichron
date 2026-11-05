namespace Anichron.Core.Domain;

/// <summary>When an asset was captured, as its file records it (ADR 0015).</summary>
/// <param name="At">The recorded clock time.</param>
/// <param name="Offset">The offset from UTC the file records with it, if any.</param>
/// <param name="Source">Which kind of source the date came from.</param>
/// <param name="Zone">The IANA time zone at the asset's position, if it has one.</param>
public sealed record CaptureDate(LocalDateTime At, Offset? Offset, CaptureSource Source, string? Zone);
