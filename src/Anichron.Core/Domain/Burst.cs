namespace Anichron.Core.Domain;

public class Burst
{
    public Guid Id { get; set; }

    /// <summary>
    /// The stored cover: the earliest member. Null once the cover row is deleted, until burst
    /// detection repairs the burst.
    /// </summary>
    public Guid? PrimaryAssetId { get; set; }

    public Instant CreatedAt { get; set; }

    // Navigation Properties
    public virtual MediaAsset? PrimaryAsset { get; set; }
    public virtual ICollection<MediaAsset> Assets { get; set; } = [];
}
