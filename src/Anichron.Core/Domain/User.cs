namespace Anichron.Core.Domain;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    // Navigation Properties
    public virtual ICollection<UserStorageConfig> StorageConfigs { get; set; } = [];
    public virtual ICollection<AssetInteraction> Interactions { get; set; } = [];
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}