using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>What extraction read from a file.</summary>
/// <param name="Recorded">The capture date the file records, if any.</param>
/// <param name="Description">Everything else it read.</param>
internal sealed record ExtractedMedia(RecordedDate? Recorded, MediaDescription Description)
{
    internal static ExtractedMedia Empty => new(null, MediaDescription.Unknown);
}
