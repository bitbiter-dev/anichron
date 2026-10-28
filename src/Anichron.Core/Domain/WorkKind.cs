namespace Anichron.Core.Domain;

public enum WorkKind
{
    TokenCleanup = 1,
    WorkItemRetention = 2,
    Crawl = 3,
    BurstDetection = 4,
    ProxySweep = 5,
    ProxyDeletion = 6,
}
