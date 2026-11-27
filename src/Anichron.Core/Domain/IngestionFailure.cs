namespace Anichron.Core.Domain;

/// <summary>A file of a storage config whose ingestion failed on its last attempted crawl.</summary>
public class IngestionFailure
{
    public const int ReasonMaxLength = 500;

    public Guid StorageConfigId { get; set; }

    /// <summary>The item's primary path, relative to the config root: a Live Photo's photo.</summary>
    public required string FilePath { get; set; }

    /// <summary>The length and last-write time of the item's files when the failed attempt began.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>Consecutive crawls whose attempt failed on this fingerprint.</summary>
    public int Failures { get; set; }

    public required string Reason { get; set; }
    public Instant FirstFailedAt { get; set; }
    public Instant LastFailedAt { get; set; }

    /// <summary>A crawl before this time skips the file while its fingerprint holds.</summary>
    public Instant RetryAfter { get; set; }
}
