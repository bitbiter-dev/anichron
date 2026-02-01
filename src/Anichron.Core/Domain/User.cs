namespace Anichron.Core.Domain;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;

    // Navigation Properties
    public virtual ICollection<UserStorageConfig> StorageConfigs { get; set; } = [];
    public virtual ICollection<AssetInteraction> Interactions { get; set; } = [];
}