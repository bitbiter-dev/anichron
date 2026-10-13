namespace Anichron.Core.Domain;

public class User
{
    public Guid Id { get; set; }
    // Stryker disable once String
    public string Username { get; set; } = string.Empty;
    // Stryker disable once String
    public string Email { get; set; } = string.Empty;
    // Stryker disable once String
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLoginAttempts { get; set; }
    public Instant? LockedUntil { get; set; }

    // Navigation Properties
    public virtual ICollection<UserStorageConfig> StorageConfigs { get; set; } = [];
    public virtual ICollection<AssetInteraction> Interactions { get; set; } = [];
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
