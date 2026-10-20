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

public sealed record CrawlPayload(Guid StorageConfigId) : IWorkPayload
{
    public static WorkKind Kind => WorkKind.Crawl;

    string IWorkPayload.DedupKey => $"crawl:{StorageConfigId}";

    Guid? IWorkPayload.StorageConfigId => StorageConfigId;
}
