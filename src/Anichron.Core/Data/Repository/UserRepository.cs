using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IUserRepository
{
    Task<bool> AnyAsync(CancellationToken ct);
    Task<bool> AnyByUsernameAsync(string username, CancellationToken ct);
    Task<bool> AnyByEmailAsync(string email, CancellationToken ct);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<User?> FindByCredentialAsync(string normalizedCredential, CancellationToken ct);
    Task<User?> FindAdminByUsernameAsync(string username, CancellationToken ct);
    Task<List<User>> FindAdminsAsync(int take, CancellationToken ct);
    Task<List<User>> GetAllAsync(CancellationToken ct);
    Task<User?> FindByIdWithConfigsAsync(Guid id, CancellationToken ct);
    void Add(User user);
    void Remove(User user);
}

public sealed class EfUserRepository(AnichronDbContext db) : IUserRepository
{
    public Task<bool> AnyAsync(CancellationToken ct)
        => db.Users.AnyAsync(ct);

    public Task<bool> AnyByUsernameAsync(string username, CancellationToken ct)
        => db.Users.AnyAsync(u => u.Username == username, ct);

    public Task<bool> AnyByEmailAsync(string email, CancellationToken ct)
        => db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
        => await db.Users.FindAsync([id], ct);

    public Task<User?> FindByCredentialAsync(string normalizedCredential, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(
            u => u.Username == normalizedCredential || u.Email == normalizedCredential, ct);

    public Task<User?> FindAdminByUsernameAsync(string username, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(u => u.IsAdmin && u.Username == username, ct);

    public Task<List<User>> FindAdminsAsync(int take, CancellationToken ct)
        => db.Users.Where(u => u.IsAdmin).Take(take).ToListAsync(ct);

    public Task<List<User>> GetAllAsync(CancellationToken ct)
        => db.Users.Include(u => u.StorageConfigs).ToListAsync(ct);

    public Task<User?> FindByIdWithConfigsAsync(Guid id, CancellationToken ct)
        => db.Users.Include(u => u.StorageConfigs).FirstOrDefaultAsync(u => u.Id == id, ct);

    public void Add(User user)
        => db.Users.Add(user);

    public void Remove(User user)
        => db.Users.Remove(user);
}
