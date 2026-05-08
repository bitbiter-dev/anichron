using Anichron.API.Security;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Services;

public interface IAdminResetService
{
    Task ResetIfRequestedAsync(CancellationToken ct);
}

public sealed partial class AdminResetService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<AdminResetService> logger) : IAdminResetService
{
    public async Task ResetIfRequestedAsync(CancellationToken ct)
    {
        var resetPassword = configuration["ADMIN_RESET_PASSWORD"];
        if (string.IsNullOrEmpty(resetPassword))
            return;

        var targetUsername = configuration["ADMIN_RESET_USERNAME"]?.Trim().ToLowerInvariant();
        User? admin;

        if (!string.IsNullOrEmpty(targetUsername))
        {
            admin = await users.FindAdminByUsernameAsync(targetUsername, ct);
            if (admin is null)
            {
                Log.AdminNotFoundByUsername(logger, targetUsername);
                return;
            }
        }
        else
        {
            var admins = await users.FindAdminsAsync(take: 2, ct);
            switch (admins.Count)
            {
                case 0:
                    Log.NoAdminFound(logger);
                    return;
                case > 1:
                    Log.MultipleAdminsFound(logger);
                    return;
                default:
                    admin = admins[0];
                    break;
            }
        }

        admin.PasswordHash = passwordHasher.Hash(resetPassword);
        admin.MustChangePassword = true;
        await unitOfWork.SaveChangesAsync(ct);
        Log.AdminPasswordReset(logger, admin.Username);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "ADMIN_RESET_PASSWORD is set but no admin with username '{Username}' was found.")]
        public static partial void AdminNotFoundByUsername(ILogger logger, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "ADMIN_RESET_PASSWORD is set but no admin user was found.")]
        public static partial void NoAdminFound(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "ADMIN_RESET_PASSWORD is set but multiple admins exist. Set ADMIN_RESET_USERNAME to specify which admin to reset.")]
        public static partial void MultipleAdminsFound(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Admin password has been reset for '{Username}'. Remove ADMIN_RESET_PASSWORD after logging in.")]
        public static partial void AdminPasswordReset(ILogger logger, string username);
    }
}
