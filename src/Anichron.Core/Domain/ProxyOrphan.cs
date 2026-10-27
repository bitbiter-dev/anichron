namespace Anichron.Core.Domain;

public class ProxyOrphan
{
    public Guid Id { get; set; }
    public Guid SweepId { get; set; }
    public required string ProxyPath { get; set; }

    /// <summary>The storage config named by the orphan's directory, or null when its path names none.</summary>
    public Guid? StorageConfigId { get; set; }

    public long SizeBytes { get; set; }
    public Instant LastWriteTime { get; set; }

    /// <summary>What a deletion did with the file, or null until one processed it.</summary>
    public ProxyDeletionOutcome? DeletionOutcome { get; set; }

    public Instant? ProcessedAt { get; set; }
}
