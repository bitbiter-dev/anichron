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

/// <summary>Makes the config's missing compact previews, one chunk per item.</summary>
/// <param name="StorageConfigId">The config whose assets get one.</param>
/// <param name="After">Only assets whose id comes after this one; null starts at the first.</param>
public sealed record CompactPreviewPayload(Guid StorageConfigId, Guid? After) : IStorageConfigPayload<CompactPreviewPayload>
{
    public static WorkKind Kind => WorkKind.CompactPreview;

    public static CompactPreviewPayload For(Guid storageConfigId) => new(storageConfigId, null);

    string IWorkPayload.DedupKey => $"compact-preview:{StorageConfigId}";

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

/// <summary>Forgets soft-deleted assets of one config, each only if it is still soft-deleted.</summary>
/// <param name="StorageConfigId">The config the assets belong to.</param>
/// <param name="AssetIds">The assets to purge.</param>
public sealed record AssetPurgePayload(Guid StorageConfigId, IReadOnlyList<Guid> AssetIds) : IWorkPayload
{
    public static WorkKind Kind => WorkKind.AssetPurge;

    Guid? IWorkPayload.StorageConfigId => StorageConfigId;
}
