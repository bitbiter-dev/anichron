using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IUserRepository
{
    Task<bool> AnyAsync(CancellationToken ct);
    Task<bool> AnyByUsernameAsync(string username, CancellationToken ct);
    Task<bool> AnyByEmailAsync(string email, CancellationToken ct);
    Task<User?> FindByCredentialAsync(string normalizedCredential, CancellationToken ct);
    Task<User?> FindAdminByUsernameAsync(string username, CancellationToken ct);
    Task<List<User>> FindAdminsAsync(int take, CancellationToken ct);
    void Add(User user);
}

public sealed class EfUserRepository(AnichronDbContext db) : IUserRepository
{
    public Task<bool> AnyAsync(CancellationToken ct)
        => db.Users.AnyAsync(ct);

    public Task<bool> AnyByUsernameAsync(string username, CancellationToken ct)
        => db.Users.AnyAsync(u => u.Username == username, ct);

    public Task<bool> AnyByEmailAsync(string email, CancellationToken ct)
        => db.Users.AnyAsync(u => u.Email == email, ct);

    public Task<User?> FindByCredentialAsync(string normalizedCredential, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(
            u => u.Username == normalizedCredential || u.Email == normalizedCredential, ct);

    public Task<User?> FindAdminByUsernameAsync(string username, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(u => u.IsAdmin && u.Username == username, ct);

    public Task<List<User>> FindAdminsAsync(int take, CancellationToken ct)
        => db.Users.Where(u => u.IsAdmin).Take(take).ToListAsync(ct);

    public void Add(User user)
        => db.Users.Add(user);
}
