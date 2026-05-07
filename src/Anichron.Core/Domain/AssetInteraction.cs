namespace Anichron.Core.Domain;

public class AssetInteraction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AssetId { get; set; }

    // Interaction State
    public bool IsStarred { get; set; }
    public bool IsLiked { get; set; }
    public bool IsHidden { get; set; }

    // Analytics
    public Instant? LastViewed { get; set; }
    public int ViewCount { get; set; }

    // Navigation Properties
    public virtual User User { get; set; } = null!;
    public virtual MediaAsset Asset { get; set; } = null!;
}
