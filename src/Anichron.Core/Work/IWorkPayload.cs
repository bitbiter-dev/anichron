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
}
