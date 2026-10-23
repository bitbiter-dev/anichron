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
