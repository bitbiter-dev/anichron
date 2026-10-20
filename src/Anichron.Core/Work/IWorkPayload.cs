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
