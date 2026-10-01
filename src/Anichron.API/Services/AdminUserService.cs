using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Services;

public interface IAdminUserService
{
    Task<List<User>> GetAllAsync(CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<AuthResult<User>> UpdateAsync(Guid callerId, Guid targetId, bool? isAdmin, bool? isDisabled, CancellationToken ct);
    Task<AuthResult> DeleteAsync(Guid callerId, Guid targetId, CancellationToken ct);
}

public sealed class AdminUserService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IClock clock,
    ITokenService tokenService) : IAdminUserService
{
    public Task<List<User>> GetAllAsync(CancellationToken ct)
        => users.GetAllAsync(ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => users.FindByIdWithConfigsAsync(id, ct);

    public async Task<AuthResult<User>> UpdateAsync(Guid callerId, Guid targetId, bool? isAdmin, bool? isDisabled, CancellationToken ct)
    {
        if (callerId == targetId)
            return AuthResult.Fail<User>(AuthError.CannotModifySelf);

        var user = await users.FindByIdWithConfigsAsync(targetId, ct);
        if (user is null)
            return AuthResult.Fail<User>(AuthError.UserNotFound);

        if (!isAdmin.HasValue && !isDisabled.HasValue)
            return AuthResult.Ok(user);

        if (isAdmin.HasValue)
            user.IsAdmin = isAdmin.Value;

        var shouldRevokeSessions = isDisabled == true && !user.IsDisabled;
        if (isDisabled.HasValue)
            user.IsDisabled = isDisabled.Value;

        // Both writes, or neither: revocation goes through ExecuteUpdateAsync, which bypasses the
        // change tracker and commits immediately, so untransacted a failing save would leave the
        // sessions revoked and the user mutation lost.
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (shouldRevokeSessions)
                await tokenService.MarkAllSessionsRevokedAsync(targetId, clock.GetCurrentInstant(), ct);

            await unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return AuthResult.Ok(user);
    }

    public async Task<AuthResult> DeleteAsync(Guid callerId, Guid targetId, CancellationToken ct)
    {
        if (callerId == targetId)
            return AuthResult.Fail(AuthError.CannotModifySelf);

        var user = await users.FindByIdAsync(targetId, ct);
        if (user is null)
            return AuthResult.Fail(AuthError.UserNotFound);

        // Same revoke-then-save pairing as UpdateAsync, transacted for the same reason — here the
        // loss would be a user whose sessions are all revoked but whose row was never deleted.
        users.Remove(user);
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await tokenService.MarkAllSessionsRevokedAsync(targetId, clock.GetCurrentInstant(), ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return AuthResult.Ok();
    }
}
