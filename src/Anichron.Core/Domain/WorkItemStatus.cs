namespace Anichron.Core.Domain;

public enum WorkItemStatus
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
