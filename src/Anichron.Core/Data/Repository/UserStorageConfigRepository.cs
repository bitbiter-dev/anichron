using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IUserStorageConfigRepository
{
    Task<UserStorageConfig?> FindByRootPathAsync(string rootPath, CancellationToken ct);
    Task<List<UserStorageConfig>> GetAllActiveAsync(CancellationToken ct);
    Task<List<UserStorageConfig>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct);
    void Add(UserStorageConfig config);
}

public sealed class EfUserStorageConfigRepository(AnichronDbContext db) : IUserStorageConfigRepository
{
    public Task<UserStorageConfig?> FindByRootPathAsync(string rootPath, CancellationToken ct)
        => db.StorageConfigs.FirstOrDefaultAsync(s => s.RootPath == rootPath, ct);

    public Task<List<UserStorageConfig>> GetAllActiveAsync(CancellationToken ct)
        => db.StorageConfigs.Where(s => s.IsActive).ToListAsync(ct);

    public Task<List<UserStorageConfig>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct)
        => db.StorageConfigs.Where(s => s.UserId == userId && s.IsActive).ToListAsync(ct);

    public void Add(UserStorageConfig config)
        => db.StorageConfigs.Add(config);
}
