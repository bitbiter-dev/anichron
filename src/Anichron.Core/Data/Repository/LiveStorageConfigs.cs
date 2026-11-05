using Anichron.Core.Domain;

namespace Anichron.Core.Data.Repository;

public static class LiveStorageConfigs
{
    extension(IQueryable<UserStorageConfig> configs)
    {
        /// <summary>The ids of the storage configs that still exist, active or not: the configs an orphan's scope can be.</summary>
        public IQueryable<Guid?> LiveIds() => configs.Select(c => (Guid?)c.Id);
    }
}
