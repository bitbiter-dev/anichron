using Anichron.API.Security;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using System.Security.Cryptography;

namespace Anichron.API.Services;

public sealed record AdminUserPasswordReset(string TemporaryPassword);

public interface IAdminResetService
{
    Task<AdminUserPasswordReset?> ResetUserPasswordAsync(Guid userId, CancellationToken ct);
}

public sealed class AdminResetService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPasswordHasher passwordHasher,
    ITokenService tokenService) : IAdminResetService
{
    public async Task<AdminUserPasswordReset?> ResetUserPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return null;

        var temporaryPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));
        user.PasswordHash = passwordHasher.Hash(temporaryPassword);
        user.MustChangePassword = true;
        var now = clock.GetCurrentInstant();

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await tokenService.MarkAllSessionsRevokedAsync(userId, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return new AdminUserPasswordReset(temporaryPassword);
    }
}
