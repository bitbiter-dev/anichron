using Anichron.Core.Data;
using Anichron.Core.Domain;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Anichron.Core.Services;

public sealed class AuthService(AnichronDbContext db, IOptions<JwtSettings> options, IClock clock) : IAuthService
{
    private readonly JwtSettings _settings = options.Value;

    public async Task<AuthResult<AuthTokens>> RegisterAsync(string username, string email, string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return AuthResult.Fail<AuthTokens>(AuthError.UsernameTaken);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return AuthResult.Fail<AuthTokens>(AuthError.EmailTaken);

        var isFirstUser = !await db.Users.AnyAsync(ct);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = HashPassword(password),
            IsAdmin = isFirstUser,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return AuthResult.Ok(await IssueTokensAsync(user, ct));
    }

    public async Task<AuthResult<AuthTokens>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Username == usernameOrEmail || u.Email == usernameOrEmail, ct);

        if (user is null || !VerifyPassword(password, user.PasswordHash))
            return AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials);

        return AuthResult.Ok(await IssueTokensAsync(user, ct));
    }

    public async Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(rawToken);
        var now = clock.GetCurrentInstant();

        var stored = await db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (stored is null)
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);

        if (stored.RevokedAt.HasValue || stored.ExpiresAt <= now)
            return AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid);

        // Rotate: revoke old token
        stored.RevokedAt = now;

        var tokens = await IssueTokensAsync(stored.User, ct);
        await db.SaveChangesAsync(ct);

        return AuthResult.Ok(tokens);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(rawToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);
        if (stored is null)
            return;

        if (stored.RevokedAt.HasValue)
            return;

        stored.RevokedAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthTokens> IssueTokensAsync(User user, CancellationToken ct)
    {
        var rawToken = GenerateRefreshToken();
        var now = clock.GetCurrentInstant();

        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromDays(_settings.RefreshTokenDays)),
        };

        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);

        return new AuthTokens(CreateJwt(user), rawToken);
    }

    private string CreateJwt(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("is_admin", user.IsAdmin ? "true" : "false"),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = RunArgon2id(Encoding.UTF8.GetBytes(password), salt);

        var combined = new byte[salt.Length + hash.Length];
        salt.CopyTo(combined, 0);
        hash.CopyTo(combined, salt.Length);
        return Convert.ToBase64String(combined);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var combined = Convert.FromBase64String(storedHash);
        var salt = combined[..16];
        var expected = combined[16..];
        var actual = RunArgon2id(Encoding.UTF8.GetBytes(password), salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] RunArgon2id(byte[] password, byte[] salt)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = 4,
            Iterations = 3,
            MemorySize = 65536,
        };
        return argon2.GetBytes(32);
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string rawToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
