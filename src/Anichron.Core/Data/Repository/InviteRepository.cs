using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IInviteRepository
{
    Task<Invite?> FindValidByHashAsync(string tokenHash, Instant now, CancellationToken ct);
    void Add(Invite invite);
}

public sealed class EfInviteRepository(AnichronDbContext db) : IInviteRepository
{
    public Task<Invite?> FindValidByHashAsync(string tokenHash, Instant now, CancellationToken ct)
        => db.Invites.FirstOrDefaultAsync(
            i => i.TokenHash == tokenHash && i.UsedAt == null && i.ExpiresAt >= now, ct);

    public void Add(Invite invite)
        => db.Invites.Add(invite);
}
