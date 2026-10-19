using System.Collections.Concurrent;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed class DuplicateCopyTally
{
    private readonly ConcurrentDictionary<Guid, int> copies = new();

    public void Count(Guid storageConfigId) => copies.AddOrUpdate(storageConfigId, 1, (_, count) => count + 1);

    public int Take(Guid storageConfigId) => copies.TryRemove(storageConfigId, out var count) ? count : 0;
}
