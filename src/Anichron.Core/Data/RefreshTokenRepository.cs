using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashWithUserAsync(string tokenHash, CancellationToken ct);
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    void Add(RefreshToken token);
    Task RevokeAllActiveByUserIdAsync(Guid userId, Instant revokedAt, CancellationToken ct);
}

public sealed class EfRefreshTokenRepository(AnichronDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashWithUserAsync(string tokenHash, CancellationToken ct)
        => db.RefreshTokens.Include(r => r.User).FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        => db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public void Add(RefreshToken token)
        => db.RefreshTokens.Add(token);

    public Task RevokeAllActiveByUserIdAsync(Guid userId, Instant revokedAt, CancellationToken ct)
        => db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);
}
