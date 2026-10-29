using Anichron.Core.Data.Repository;

namespace Anichron.Core.Work;

/// <summary>
/// The deletions a request queues, in queue order: one per live storage config, by id, then one unscoped
/// deletion for orphans of a config that no longer exists or of none.
/// </summary>
public static class ProxyDeletionPlan
{
    /// <summary>Deletions of every unprocessed orphan of the sweep, with no selection.</summary>
    /// <param name="sweepId">The sweep.</param>
    /// <param name="pendingConfigs">Each live config, null for none, that the sweep's unprocessed orphans name.</param>
    public static IReadOnlyList<ProxyDeletionPayload> ForAll(Guid sweepId, IEnumerable<Guid?> pendingConfigs)
        => [.. InQueueOrder(pendingConfigs.Distinct(), config => config)
            .Select(config => new ProxyDeletionPayload(sweepId, config, null))];

    /// <summary>Deletions of the selected orphans, each holding the ids of its scope's orphans.</summary>
    /// <param name="sweepId">The sweep.</param>
    /// <param name="selected">The selected orphans and their live configs.</param>
    public static IReadOnlyList<ProxyDeletionPayload> ForSelection(Guid sweepId, IEnumerable<ProxyOrphanScope> selected)
        => [.. InQueueOrder(selected.GroupBy(o => o.LiveStorageConfigId), group => group.Key)
            .Select(group => new ProxyDeletionPayload(sweepId, group.Key, [.. group.Select(o => o.Id)]))];

    private static IOrderedEnumerable<T> InQueueOrder<T>(IEnumerable<T> scopes, Func<T, Guid?> configOf)
        => scopes.OrderBy(s => configOf(s) is null).ThenBy(configOf);
}
