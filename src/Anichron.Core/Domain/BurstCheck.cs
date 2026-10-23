namespace Anichron.Core.Domain;

public class BurstCheck
{
    public Guid AssetId { get; set; }
    public virtual MediaAsset Asset { get; set; } = null!;

    public Instant CheckedAt { get; set; }

    /// <summary>
    /// The photo has an EXIF capture time, a camera make and model, and a thumbnail, so it can be
    /// grouped.
    /// </summary>
    public bool IsCandidate { get; set; }

    /// <summary>Apple's BurstUUID, read from the original's maker note.</summary>
    public string? CameraBurstId { get; set; }
}
