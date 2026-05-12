namespace Anichron.Core.Domain;

public class Invite
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
    public Instant ExpiresAt { get; set; }
    public Instant? UsedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? UsedByUserId { get; set; }
    public virtual User CreatedBy { get; set; } = null!;
    public virtual User? UsedBy { get; set; }
}
