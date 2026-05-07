using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.API.Services;

public sealed record AuthTokens(string AccessToken, string RefreshToken);

public enum AuthError
{
    None = 0,
    UsernameTaken = 1,
    EmailTaken = 2,
    InvalidCredentials = 3,
    TokenInvalid = 4,
    InvalidUsername = 5,
    InvalidEmail = 6,
    PasswordTooShort = 7,
    PasswordTooLong = 8,
    PasswordPwned = 9,
    AccountDisabled = 10,
    AccountTemporarilyLocked = 11,
}

public sealed record AuthResult<T>
{
    public T? Value { get; init; }
    public AuthError? Error { get; init; }
    public bool IsSuccess => Error is null;
    public int? RetryAfterSeconds { get; init; }
}

public static class AuthResult
{
    public static AuthResult<T> Ok<T>(T value) => new() { Value = value };
    public static AuthResult<T> Fail<T>(AuthError error) => new() { Error = error };
    public static AuthResult<T> Locked<T>(int retryAfterSeconds) => new()
    {
        Error = AuthError.AccountTemporarilyLocked,
        RetryAfterSeconds = retryAfterSeconds,
    };
}

public interface IAuthService
{
    Task<AuthResult<AuthTokens>> RegisterAsync(string username, string email, string password, CancellationToken ct);
    Task<AuthResult<AuthTokens>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct);
    Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct);
    Task RevokeAsync(string rawToken, CancellationToken ct);
}

public sealed class AuthService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IClock clock,
    IGuidFactory guidFactory,
    IPasswordHasher passwordHasher,
    IRegistrationValidator validator,
    ITokenService tokenService)
    : IAuthService
{
    private readonly string _dummyPasswordHash = passwordHasher.Hash(guidFactory.NewGuid().ToString());

    public async Task<AuthResult<AuthTokens>> RegisterAsync(string username, string email, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);

        var normalizedUsername = username.Trim().ToLowerInvariant();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var error = await validator.ValidateAsync(normalizedUsername, normalizedEmail, password, ct);
        if (error is not null)
            return AuthResult.Fail<AuthTokens>(error.Value);

        if (await users.AnyByUsernameAsync(normalizedUsername, ct))
            return AuthResult.Fail<AuthTokens>(AuthError.UsernameTaken);

        if (await users.AnyByEmailAsync(normalizedEmail, ct))
            return AuthResult.Fail<AuthTokens>(AuthError.EmailTaken);

        var user = new User
        {
            Id = guidFactory.NewGuid(),
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(password),
        };

        users.Add(user);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            return pg.ConstraintName?.Contains("Email", StringComparison.OrdinalIgnoreCase) == true
                ? AuthResult.Fail<AuthTokens>(AuthError.EmailTaken)
                : AuthResult.Fail<AuthTokens>(AuthError.UsernameTaken);
        }

        return AuthResult.Ok(await tokenService.IssueAsync(user, ct));
    }

    public async Task<AuthResult<AuthTokens>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usernameOrEmail);
        ArgumentNullException.ThrowIfNull(password);

        var normalized = usernameOrEmail.Trim().ToLowerInvariant();
        var user = await users.FindByCredentialAsync(normalized, ct);

        // Prevent timing attack: Use dummy hash for non-existing accounts
        var passwordValid = passwordHasher.Verify(password, user?.PasswordHash ?? _dummyPasswordHash);

        var now = clock.GetCurrentInstant();

        // Record failed attempt only if not already locked
        // Prevents counter growth and redundant DB writes during an active lockout.
        if (user is not null && !passwordValid && (user.LockedUntil is null || user.LockedUntil <= now))
            await RecordFailedLoginAttemptAsync(user, now, ct);

        if (user is null || !passwordValid)
            return AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials);

        if (user.IsDisabled)
            return AuthResult.Fail<AuthTokens>(AuthError.AccountDisabled);

        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            var secondsRemaining = (int)Math.Ceiling((lockedUntil - now).TotalSeconds);
            return AuthResult.Locked<AuthTokens>(Math.Max(1, secondsRemaining));
        }

        // Counter reset and token issuance share one transaction — if IssueAsync fails,
        // the counter is not persisted so the user's lockout state is preserved correctly.
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        var tokens = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.SaveChangesAsync(ct);
            return await tokenService.IssueAsync(user, ct);
        }, ct);

        return AuthResult.Ok(tokens);
    }

    public Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct)
        => tokenService.RefreshAsync(rawToken, ct);

    public Task RevokeAsync(string rawToken, CancellationToken ct)
        => tokenService.RevokeAsync(rawToken, ct);

    private async Task RecordFailedLoginAttemptAsync(User user, Instant now, CancellationToken ct)
    {
        user.FailedLoginAttempts++;
        var backoff = ComputeBackoffSeconds(user.FailedLoginAttempts);
        user.LockedUntil = now.Plus(Duration.FromSeconds(backoff));
        await unitOfWork.SaveChangesAsync(ct);
    }

    private const int AllowedAttempts = AppDefaults.Lockout.AllowedAttempts;
    private const int MaxAttempts = AppDefaults.Lockout.MaxAttempts;
    private const int MaxLockoutSeconds = AppDefaults.Lockout.MaxSeconds;
    private const int BackoffBase = AppDefaults.Lockout.BackoffBase;

    private static int ComputeBackoffSeconds(int failedAttempts) => failedAttempts switch
    {
        <= AllowedAttempts => 0,
        >= MaxAttempts => MaxLockoutSeconds,
        _ => (int)Math.Pow(BackoffBase, failedAttempts - AllowedAttempts),
    };
}
