namespace Anichron.Core.Domain;

public class MediaAsset
{
    public Guid Id { get; set; }
    public Guid StorageConfigId { get; set; }
    public Guid? BurstId { get; set; }

    /// <summary>
    /// Links this asset to its paired secondary asset (e.g. a HEIC photo linked to its Live Photo MOV counterpart).
    /// </summary>
    public Guid? PairedAssetId { get; set; }

    // File Information
    public string FilePath { get; set; } = string.Empty; // Relative to StorageConfig Root
    public string FileName { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // XXHash64 for move tracking

    // Temporal Data (Optimized for "On This Day")
    public LocalDateTime DateCaptured { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public int Year { get; set; }

    // Metadata & State
    public MediaType MediaType { get; set; }
    public bool IsSoftDeleted { get; set; }
    public Instant LastSeenOnNas { get; set; }

    // --- Navigation Properties ---

    public virtual UserStorageConfig StorageConfig { get; set; } = null!;

    public virtual Burst? Burst { get; set; }

    /// <summary>
    /// Navigation property for the paired secondary asset.
    /// </summary>
    public virtual MediaAsset? PairedAsset { get; set; }

    public virtual Metadata? Metadata { get; set; }

    public virtual ICollection<ProxyFile> ProxyFiles { get; set; } = [];

    public virtual ICollection<AssetInteraction> Interactions { get; set; } = [];
}
