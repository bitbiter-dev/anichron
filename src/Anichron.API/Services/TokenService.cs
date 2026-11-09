using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;
using System.Diagnostics;
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
    private static Duration JustRotatedWindow => Duration.FromSeconds(30);

    private readonly JwtSettings settings = options.Value;

    public AuthTokens Issue(User user)
    {
        var (stored, issued) = NewSession(user);
        tokens.Add(stored);
        return issued;
    }

    public async Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct)
    {
        var tokenHash = HashToken(rawToken);
        return await TryRefreshAsync(tokenHash, ct) ?? await TryRefreshAsync(tokenHash, ct)
            ?? throw new UnreachableException("A token that lost its rotation is revoked when read again.");
    }

    private async Task<AuthResult<AuthTokens>?> TryRefreshAsync(string tokenHash, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();

        var stored = await tokens.FindByHashWithUserAsync(tokenHash, ct);

        if (stored is null)
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);

        var rotating = stored.RevokedAt is { } revokedAt ? JustRotatedInto(stored, revokedAt, now) : stored;
        if (rotating is null)
            return await RevokeEverySessionAsync(stored.UserId, now, ct);

        if (stored.ExpiresAt <= now)
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);

        if (stored.User.IsDisabled)
            return AuthResult.Fail<AuthTokens>(AuthError.AccountDisabled);

        if (stored.User.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return AuthResult.Locked<AuthTokens>(
                Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalSeconds)));
        }

        var (successor, issued) = NewSession(stored.User);
        if (await tokens.TryRotateAsync(rotating.Id, successor, ct))
            return AuthResult.Ok(issued);

        return rotating == stored ? null : await RevokeEverySessionAsync(stored.UserId, now, ct);
    }

    private static RefreshToken? JustRotatedInto(RefreshToken stored, Instant revokedAt, Instant now)
        => stored.ReplacedBy is { RevokedAt: null } successor && now - revokedAt <= JustRotatedWindow ? successor : null;

    private async Task<AuthResult<AuthTokens>> RevokeEverySessionAsync(Guid userId, Instant now, CancellationToken ct)
    {
        await tokens.RevokeAllActiveByUserIdAsync(userId, now, ct);
        return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);
    }

    private (RefreshToken Stored, AuthTokens Issued) NewSession(User user)
    {
        var rawToken = GenerateRefreshToken();
        var now = clock.GetCurrentInstant();

        var stored = new RefreshToken
        {
            Id = guidFactory.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromDays(settings.RefreshTokenDays)),
        };

        return (stored, new AuthTokens(jwtFactory.Create(user), rawToken, settings.AccessTokenLifetime, user));
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
