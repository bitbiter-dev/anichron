namespace Anichron.Core.Domain;

public class Burst
{
    public Guid Id { get; set; }

    /// <summary>
    /// Points to the specific asset that should represent this burst in the UI.
    /// </summary>
    public Guid PrimaryAssetId { get; set; }

    public Instant CreatedAt { get; set; }

    // Navigation Properties
    public virtual MediaAsset PrimaryAsset { get; set; } = null!;
    public virtual ICollection<MediaAsset> Assets { get; set; } = [];
}