using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Anichron.API.Services;

public interface ITokenService
{
    AuthTokens Issue(User user);
    Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct);
    Task RevokeAsync(string rawToken, CancellationToken ct);
    Task MarkAllSessionsRevokedAsync(Guid userId, Instant revokedAt, CancellationToken ct);
}

public sealed class TokenService(
    IRefreshTokenRepository tokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    IGuidFactory guidFactory,
    IOptions<JwtSettings> options,
    IJwtFactory jwtFactory) : ITokenService
{
    private readonly JwtSettings settings = options.Value;

    public AuthTokens Issue(User user)
    {
        var rawToken = GenerateRefreshToken();
        var now = clock.GetCurrentInstant();

        tokens.Add(new RefreshToken
        {
            Id = guidFactory.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromDays(settings.RefreshTokenDays)),
        });

        return new AuthTokens(jwtFactory.Create(user), rawToken);
    }

    public async Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct)
    {
        var tokenHash = HashToken(rawToken);
        var now = clock.GetCurrentInstant();

        var stored = await tokens.FindByHashWithUserAsync(tokenHash, ct);

        if (stored is null)
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);

        if (stored.RevokedAt.HasValue)
        {
            // Revoked token replayed — possible theft, wipe all sessions
            await tokens.RevokeAllActiveByUserIdAsync(stored.UserId, now, ct);
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);
        }

        if (stored.ExpiresAt <= now)
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);

        if (stored.User.IsDisabled)
            return AuthResult.Fail<AuthTokens>(AuthError.AccountDisabled);

        if (stored.User.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return AuthResult.Locked<AuthTokens>(
                Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalSeconds)));
        }

        stored.RevokedAt = now;
        var issued = Issue(stored.User);
        await unitOfWork.SaveChangesAsync(ct);

        return AuthResult.Ok(issued);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var tokenHash = HashToken(rawToken);
        var stored = await tokens.FindByHashAsync(tokenHash, ct);
        if (stored is null)
            return;
        if (stored.RevokedAt.HasValue)
            return;

        stored.RevokedAt = clock.GetCurrentInstant();
        await unitOfWork.SaveChangesAsync(ct);
    }

    public Task MarkAllSessionsRevokedAsync(Guid userId, Instant revokedAt, CancellationToken ct)
        => tokens.RevokeAllActiveByUserIdAsync(userId, revokedAt, ct);

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string rawToken)
        => Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(rawToken)));
}
