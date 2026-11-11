using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Services;

public interface IAdminUserService
{
    Task<List<User>> GetAllAsync(CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<User, IUpdateUserError>> UpdateAsync(Guid callerId, Guid targetId, bool? isAdmin, bool? isDisabled, CancellationToken ct);
    Task<IDeleteUserError?> DeleteAsync(Guid callerId, Guid targetId, CancellationToken ct);
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

    public async Task<ServiceResult<User, IUpdateUserError>> UpdateAsync(Guid callerId, Guid targetId, bool? isAdmin, bool? isDisabled, CancellationToken ct)
    {
        if (callerId == targetId)
            return new CannotModifySelf();

        var user = await users.FindByIdWithConfigsAsync(targetId, ct);
        if (user is null)
            return new UserNotFound();

        if (!isAdmin.HasValue && !isDisabled.HasValue)
            return user;

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

        return user;
    }

    public async Task<IDeleteUserError?> DeleteAsync(Guid callerId, Guid targetId, CancellationToken ct)
    {
        if (callerId == targetId)
            return new CannotModifySelf();

        var user = await users.FindByIdAsync(targetId, ct);
        if (user is null)
            return new UserNotFound();

        // Same revoke-then-save pairing as UpdateAsync, transacted for the same reason — here the
        // loss would be a user whose sessions are all revoked but whose row was never deleted.
        users.Remove(user);
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await tokenService.MarkAllSessionsRevokedAsync(targetId, clock.GetCurrentInstant(), ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return null;
    }
}
