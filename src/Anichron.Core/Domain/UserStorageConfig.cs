namespace Anichron.Core.Domain;

public class UserStorageConfig
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string RootPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    // Navigation Properties
    public virtual User User { get; set; } = null!;
    public virtual ICollection<MediaAsset> Assets { get; set; } = [];
}
