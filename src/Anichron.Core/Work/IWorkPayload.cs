using Anichron.Core.Domain;

namespace Anichron.Core.Work;

public interface IWorkPayload
{
    static abstract WorkKind Kind { get; }

    /// <summary>
    /// Coalesces requests: while an item with this key is queued, enqueueing another returns it.
    /// Null, the default, keeps every request a separate item.
    /// </summary>
    string? DedupKey => null;

    /// <summary>
    /// The storage config the work is scoped to, or null for work any Worker may run. A per-user
    /// Worker claims only its user's configs and unscoped work.
    /// </summary>
    Guid? StorageConfigId => null;
}

/// <summary>Work that runs once per storage config, periodically for every config a Worker serves.</summary>
/// <typeparam name="TSelf">The payload type itself.</typeparam>
public interface IStorageConfigPayload<out TSelf> : IWorkPayload
    where TSelf : IStorageConfigPayload<TSelf>
{
    static abstract TSelf For(Guid storageConfigId);
}
