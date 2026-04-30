using Anichron.Core.Common;

namespace Anichron.Core.Domain;

public class MediaAsset
{
    public Guid Id { get; set; }
    public Guid StorageConfigId { get; set; }
    public Guid? BurstId { get; set; }

    /// <summary>
    /// Self-referencing ID to link a photo (.HEIC) with its video (.MOV) counterpart.
    /// </summary>
    public Guid? LivePhotoPairId { get; set; }

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
    /// The paired video for a Live Photo.
    /// </summary>
    public virtual MediaAsset? LivePhotoPair { get; set; }

    public virtual Metadata? Metadata { get; set; }

    public virtual ICollection<ProxyFile> ProxyFiles { get; set; } = [];

    public virtual ICollection<AssetInteraction> Interactions { get; set; } = [];
}