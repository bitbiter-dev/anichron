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

public sealed record AuthTokens(string AccessToken, string RefreshToken, TimeSpan AccessTokenLifetime, User User);
public sealed record AdminCreatedUser(Guid Id, string Username, string Email, string TemporaryPassword);

public interface IAuthService
{
    Task<ServiceResult<AuthTokens, IRegisterError>> RegisterAsync(string username, string email, string password, string inviteToken, CancellationToken ct);
    Task<ServiceResult<AuthTokens, ILoginError>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct);
    Task<ServiceResult<AuthTokens, IRefreshError>> RefreshAsync(string rawToken, CancellationToken ct);
    Task RevokeAsync(string rawToken, CancellationToken ct);
    Task<ServiceResult<AuthTokens, IChangePasswordError>> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
    Task<ServiceResult<AdminCreatedUser, IAdminCreateUserError>> AdminCreateUserAsync(string username, string email, CancellationToken ct);
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
    ILockoutService lockout,
    ILoginResponseFloor responseFloor)
    : IAuthService
{
    public async Task<ServiceResult<AuthTokens, IRegisterError>> RegisterAsync(string username, string email, string password, string inviteToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(inviteToken);

        var now = clock.GetCurrentInstant();
        var invite = await invites.FindValidByHashAsync(HashInviteToken(inviteToken), now, ct);
        if (invite is null)
            return new InviteTokenInvalid();

        var normalizedUsername = IdentityNormalizer.Normalize(username);
        var normalizedEmail = IdentityNormalizer.Normalize(email);

        var error = await validator.ValidateAsync(normalizedUsername, normalizedEmail, password, ct);
        if (error is not null)
            return ServiceResult.Fail<AuthTokens, IRegisterError>(error);

        if (await users.AnyByUsernameAsync(normalizedUsername, ct))
            return new UsernameTaken();

        if (await users.AnyByEmailAsync(normalizedEmail, ct))
            return new EmailTaken();

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

        var tokens = tokenService.Issue(user);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return tokens;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new InviteTokenInvalid();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } postgresException)
        {
            return ServiceResult.Fail<AuthTokens, IRegisterError>(DetectConstraintError(postgresException));
        }
    }

    internal static string HashInviteToken(string rawToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public async Task<ServiceResult<AuthTokens, ILoginError>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usernameOrEmail);
        ArgumentNullException.ThrowIfNull(password);

        var startedAt = responseFloor.Start();
        var result = await AuthenticateAsync(usernameOrEmail, password, ct);
        if (!result.IsSuccess)
            await responseFloor.PadAsync(startedAt, ct);

        return result;
    }

    private async Task<ServiceResult<AuthTokens, ILoginError>> AuthenticateAsync(string usernameOrEmail, string password, CancellationToken ct)
    {
        var user = await users.FindByCredentialAsync(usernameOrEmail, ct);

        // A null hash means "no such account", and the hasher spends a full Argon2 pass on it
        // rather than short-circuiting. That removes the hashing asymmetry; this call site's only
        // job is not to defeat it by branching before the call.
        var passwordValid = passwordHasher.Verify(password, user?.PasswordHash);

        var now = clock.GetCurrentInstant();

        // Record failed attempt only if not already locked
        // Prevents counter growth and redundant DB writes during an active lockout.
        if (user is not null && !passwordValid && !lockout.IsLockedOut(user, now))
            await lockout.RecordFailedAttemptAsync(user, now, ct);

        if (user is null || !passwordValid)
            return new InvalidCredentials();

        if (user.IsDisabled)
            return new AccountDisabled();

        if (lockout.IsLockedOut(user, now))
        {
            // IsLockedOut guarantees LockedUntil is non-null and in the future.
            var secondsRemaining = (int)Math.Ceiling((user.LockedUntil!.Value - now).TotalSeconds);
            return new AccountLocked(Math.Max(1, secondsRemaining));
        }

        lockout.PrepareReset(user);
        var tokens = tokenService.Issue(user);
        await unitOfWork.SaveChangesAsync(ct);

        return tokens;
    }

    public Task<ServiceResult<AuthTokens, IRefreshError>> RefreshAsync(string rawToken, CancellationToken ct)
        => tokenService.RefreshAsync(rawToken, ct);

    public Task RevokeAsync(string rawToken, CancellationToken ct)
        => tokenService.RevokeAsync(rawToken, ct);

    public async Task<ServiceResult<AuthTokens, IChangePasswordError>> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || !passwordHasher.Verify(currentPassword, user.PasswordHash))
            return new CurrentPasswordWrong();

        var rejected = await validator.ValidatePasswordAsync(newPassword, ct);
        if (rejected is not null)
            return rejected;

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        var tokens = tokenService.Issue(user);
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var now = clock.GetCurrentInstant();
            await tokenService.MarkAllSessionsRevokedAsync(userId, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return tokens;
    }

    public async Task<ServiceResult<AdminCreatedUser, IAdminCreateUserError>> AdminCreateUserAsync(string username, string email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(email);

        var normalizedUsername = IdentityNormalizer.Normalize(username);
        var normalizedEmail = IdentityNormalizer.Normalize(email);

        var identityError = validator.ValidateIdentity(normalizedUsername, normalizedEmail);
        if (identityError is not null)
            return ServiceResult.Fail<AdminCreatedUser, IAdminCreateUserError>(identityError);

        if (await users.AnyByUsernameAsync(normalizedUsername, ct))
            return new UsernameTaken();

        if (await users.AnyByEmailAsync(normalizedEmail, ct))
            return new EmailTaken();

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
            return ServiceResult.Fail<AdminCreatedUser, IAdminCreateUserError>(DetectConstraintError(postgresException));
        }

        return new AdminCreatedUser(user.Id, user.Username, user.Email, temporaryPassword);
    }

    private static IIdentityRejection DetectConstraintError(PostgresException postgresException)
        => postgresException.ConstraintName switch
        {
            UserIndexNames.EmailUnique => new EmailTaken(),
            UserIndexNames.UsernameUnique => new UsernameTaken(),
            _ => throw new UnreachableException($"Unexpected unique constraint violation: {postgresException.ConstraintName}"),
        }; // ix_users_username is the only other unique constraint on User
}
