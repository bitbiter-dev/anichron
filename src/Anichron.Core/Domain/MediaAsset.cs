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

    /// <summary>Whether a photo has ever been paired with this row as its video; set at pairing and never cleared.</summary>
    public bool IsLivePhotoVideo { get; set; }

    // File Information
    // Stryker disable once String
    public string FilePath { get; set; } = string.Empty; // Relative to StorageConfig Root
    // Stryker disable once String
    public string FileName { get; set; } = string.Empty;
    // Stryker disable once String
    public string ContentHash { get; set; } = string.Empty; // XXHash64 for move tracking

    // Temporal Data (Optimized for "On This Day")
    /// <summary>The capture date as one value, set as a whole over its columns; null when the asset is undated.</summary>
    public CaptureDate? Captured
    {
        get => DateCaptured is { } at && CaptureSource is { } source ? new(at, CaptureOffset, source, CaptureZone) : null;
        set
        {
            DateCaptured = value?.At;
            CaptureOffset = value?.Offset;
            CaptureSource = value?.Source;
            CaptureZone = value?.Zone;
            Month = value?.At.Month;
            Day = value?.At.Day;
            Year = value?.At.Year;
        }
    }

    public LocalDateTime? DateCaptured { get; private set; }
    public Offset? CaptureOffset { get; private set; }
    public CaptureSource? CaptureSource { get; private set; }
    public string? CaptureZone { get; private set; }
    public int? Month { get; private set; }
    public int? Day { get; private set; }
    public int? Year { get; private set; }

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
