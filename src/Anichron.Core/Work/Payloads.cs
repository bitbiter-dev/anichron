using Anichron.Core.Domain;

namespace Anichron.Core.Work;

public sealed record TokenCleanupPayload : IWorkPayload
{
    public static WorkKind Kind => WorkKind.TokenCleanup;

    string IWorkPayload.DedupKey => "token-cleanup";
}

public sealed record WorkItemRetentionPayload : IWorkPayload
{
    public static WorkKind Kind => WorkKind.WorkItemRetention;

    string IWorkPayload.DedupKey => "work-item-retention";
}

public sealed record CrawlPayload(Guid StorageConfigId) : IStorageConfigPayload<CrawlPayload>
{
    public static WorkKind Kind => WorkKind.Crawl;

    public static CrawlPayload For(Guid storageConfigId) => new(storageConfigId);

    string IWorkPayload.DedupKey => $"crawl:{StorageConfigId}";

    Guid? IWorkPayload.StorageConfigId => StorageConfigId;
}

public sealed record BurstDetectionPayload(Guid StorageConfigId) : IStorageConfigPayload<BurstDetectionPayload>
{
    public static WorkKind Kind => WorkKind.BurstDetection;

    public static BurstDetectionPayload For(Guid storageConfigId) => new(storageConfigId);

    string IWorkPayload.DedupKey => $"burst-detection:{StorageConfigId}";

    Guid? IWorkPayload.StorageConfigId => StorageConfigId;
}

public sealed record ProxySweepPayload : IWorkPayload
{
    public static WorkKind Kind => WorkKind.ProxySweep;

    string IWorkPayload.DedupKey => "proxy-sweep";
}

/// <summary>Deletes orphans a sweep reported, each re-checked first.</summary>
/// <param name="SweepId">The sweep whose report holds the orphans.</param>
/// <param name="StorageConfigId">
/// The live config the orphans belong to, or null for orphans of no live config, which any Worker may delete.
/// </param>
/// <param name="OrphanIds">The orphans to delete, or null for every orphan of the sweep in that scope.</param>
public sealed record ProxyDeletionPayload(Guid SweepId, Guid? StorageConfigId, IReadOnlyList<Guid>? OrphanIds) : IWorkPayload
{
    public static WorkKind Kind => WorkKind.ProxyDeletion;

    Guid? IWorkPayload.StorageConfigId => StorageConfigId;
}
