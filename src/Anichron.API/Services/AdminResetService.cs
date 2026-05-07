using Anichron.API.Security;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.API.Services;

public interface IAdminResetService
{
    Task ResetIfRequestedAsync(CancellationToken ct);
}

public sealed class AdminResetService(
    AnichronDbContext db,
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
            admin = await db.Users.FirstOrDefaultAsync(u => u.IsAdmin && u.Username == targetUsername, ct);
            if (admin is null)
            {
                logger.LogError(
                    "ADMIN_RESET_PASSWORD is set but no admin with username '{Username}' was found.",
                    targetUsername);
                return;
            }
        }
        else
        {
            var admins = await db.Users.Where(u => u.IsAdmin).Take(2).ToListAsync(ct);
            switch (admins.Count)
            {
                case 0:
                    logger.LogWarning("ADMIN_RESET_PASSWORD is set but no admin user was found.");
                    return;
                case > 1:
                    logger.LogError(
                        "ADMIN_RESET_PASSWORD is set but multiple admins exist. " +
                        "Set ADMIN_RESET_USERNAME to specify which admin to reset.");
                    return;
                default:
                    admin = admins[0];
                    break;
            }
        }

        admin.PasswordHash = passwordHasher.Hash(resetPassword);
        admin.MustChangePassword = true;
        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Admin password has been reset for '{Username}'. Remove ADMIN_RESET_PASSWORD after logging in.",
            admin.Username);
    }
}
