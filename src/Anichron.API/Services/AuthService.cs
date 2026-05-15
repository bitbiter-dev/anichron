using Anichron.API.Security;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Anichron.API.Services;

public sealed record AuthTokens(string AccessToken, string RefreshToken);
public sealed record AdminCreatedUser(Guid Id, string Username, string Email, string TemporaryPassword);

public sealed record AuthResult<T>
{
    public T? Value { get; init; }
    public AuthError? Error { get; init; }
    public bool IsSuccess => Error is null;
    public int? RetryAfterSeconds { get; init; }
}

public sealed record AuthResult
{
    public AuthError? Error { get; init; }
    public bool IsSuccess => Error is null;
    public int? RetryAfterSeconds { get; init; }

    public static AuthResult Ok() => new();
    public static AuthResult<T> Ok<T>(T value) => new() { Value = value };

    public static AuthResult Fail(AuthError error) => new() { Error = error };
    public static AuthResult<T> Fail<T>(AuthError error) => new() { Error = error };

    public static AuthResult<T> Locked<T>(int retryAfterSeconds) => new()
    {
        Error = AuthError.AccountTemporarilyLocked,
        RetryAfterSeconds = retryAfterSeconds,
    };
}

public interface IAuthService
{
    Task<AuthResult<AuthTokens>> RegisterAsync(string username, string email, string password, string inviteToken, CancellationToken ct);
    Task<AuthResult<AuthTokens>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct);
    Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct);
    Task RevokeAsync(string rawToken, CancellationToken ct);
    Task<AuthResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
    Task<AuthResult<AdminCreatedUser>> AdminCreateUserAsync(string username, string email, CancellationToken ct);
}

public sealed class AuthService(
    IUserRepository users,
    IInviteRepository invites,
    IUnitOfWork unitOfWork,
    IClock clock,
    IGuidFactory guidFactory,
    IPasswordHasher passwordHasher,
    IRegistrationValidator validator,
    ITokenService tokenService,
    ILockoutService lockout)
    : IAuthService
{
    private readonly string _dummyPasswordHash = passwordHasher.Hash(guidFactory.NewGuid().ToString());

    public async Task<AuthResult<AuthTokens>> RegisterAsync(string username, string email, string password, string inviteToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(inviteToken);

        var now = clock.GetCurrentInstant();
        var invite = await invites.FindValidByHashAsync(HashInviteToken(inviteToken), now, ct);
        if (invite is null)
            return AuthResult.Fail<AuthTokens>(AuthError.InviteTokenInvalid);

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
        invite.UsedAt = now;
        invite.UsedByUserId = user.Id;

        try
        {
            return AuthResult.Ok(await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await unitOfWork.SaveChangesAsync(ct);
                return await tokenService.IssueAsync(user, ct);
            }, ct));
        }
        catch (DbUpdateConcurrencyException)
        {
            return AuthResult.Fail<AuthTokens>(AuthError.InviteTokenInvalid);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } postgresException)
        {
            return AuthResult.Fail<AuthTokens>(DetectConstraintError(postgresException));
        }
    }

    internal static string HashInviteToken(string rawToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

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
        if (user is not null && !passwordValid && !lockout.IsLockedOut(user, now))
            await lockout.RecordFailedAttemptAsync(user, now, ct);

        if (user is null || !passwordValid)
            return AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials);

        if (user.IsDisabled)
            return AuthResult.Fail<AuthTokens>(AuthError.AccountDisabled);

        if (lockout.IsLockedOut(user, now))
        {
            // IsLockedOut guarantees LockedUntil is non-null and in the future.
            var secondsRemaining = (int)Math.Ceiling((user.LockedUntil!.Value - now).TotalSeconds);
            return AuthResult.Locked<AuthTokens>(Math.Max(1, secondsRemaining));
        }

        // Counter reset and token issuance share one transaction — if IssueAsync fails,
        // the counter is not persisted so the user's lockout state is preserved correctly.
        lockout.PrepareReset(user);

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

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || !passwordHasher.Verify(currentPassword, user.PasswordHash))
            return AuthResult.Fail(AuthError.InvalidCredentials);

        var error = await validator.ValidatePasswordAsync(newPassword, ct);
        if (error is not null)
            return AuthResult.Fail(error.Value);

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            user.PasswordHash = passwordHasher.Hash(newPassword);
            user.MustChangePassword = false;
            var now = clock.GetCurrentInstant();
            await tokenService.MarkAllSessionsRevokedAsync(userId, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return AuthResult.Ok();
    }

    public async Task<AuthResult<AdminCreatedUser>> AdminCreateUserAsync(string username, string email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(email);

        var normalizedUsername = username.Trim().ToLowerInvariant();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var identityError = validator.ValidateIdentity(normalizedUsername, normalizedEmail);
        if (identityError is not null)
            return AuthResult.Fail<AdminCreatedUser>(identityError.Value);

        if (await users.AnyByUsernameAsync(normalizedUsername, ct))
            return AuthResult.Fail<AdminCreatedUser>(AuthError.UsernameTaken);

        if (await users.AnyByEmailAsync(normalizedEmail, ct))
            return AuthResult.Fail<AdminCreatedUser>(AuthError.EmailTaken);

        var temporaryPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));

        var user = new User
        {
            Id = guidFactory.NewGuid(),
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(temporaryPassword),
            MustChangePassword = true,
        };

        users.Add(user);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } postgresException)
        {
            return AuthResult.Fail<AdminCreatedUser>(DetectConstraintError(postgresException));
        }

        return AuthResult.Ok(new AdminCreatedUser(user.Id, user.Username, user.Email, temporaryPassword));
    }

    private static AuthError DetectConstraintError(PostgresException postgresException)
        => postgresException.ConstraintName switch
        {
            UserIndexNames.EmailUnique => AuthError.EmailTaken,
            UserIndexNames.UsernameUnique => AuthError.UsernameTaken,
            _ => throw new UnreachableException($"Unexpected unique constraint violation: {postgresException.ConstraintName}"),
        }; // ix_users_username is the only other unique constraint on User
}
