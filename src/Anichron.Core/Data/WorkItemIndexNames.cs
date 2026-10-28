namespace Anichron.Core.Data;

public static class WorkItemIndexNames
{
    public const string QueuedDedupKeyUnique = "ix_workitems_dedupkey_queued";

    public const string RunningStorageConfigUnique = "ix_workitems_storageconfig_running";
}
