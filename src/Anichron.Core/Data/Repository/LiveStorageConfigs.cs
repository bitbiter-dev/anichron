using Anichron.Core.Domain;

namespace Anichron.Core.Data.Repository;

public static class LiveStorageConfigs
{
    /// <summary>The ids of the storage configs that still exist, active or not: the configs an orphan's scope can be.</summary>
    /// <param name="configs">The storage configs to read.</param>
    public static IQueryable<Guid?> LiveIds(this IQueryable<UserStorageConfig> configs) => configs.Select(c => (Guid?)c.Id);
}
