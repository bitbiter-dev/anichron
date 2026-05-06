using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.API.Services;

public interface IAdminSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public sealed class AdminSeeder(
    AnichronDbContext db,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<AdminSeeder> logger) : IAdminSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedBootstrapAdminAsync(ct);
        await ResetAdminPasswordAsync(ct);
    }

    private async Task SeedBootstrapAdminAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
            return;

        var username = configuration["BOOTSTRAP_ADMIN_USERNAME"] ?? AppDefaults.Startup.AdminDefaultUsername;
        var password = configuration["BOOTSTRAP_ADMIN_PASSWORD"] ?? AppDefaults.Startup.AdminDefaultPassword;

        logger.LogWarning("No users exist. Creating bootstrap admin.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username.Trim().ToLowerInvariant(),
            Email = AppDefaults.Startup.AdminDefaultMail,
            PasswordHash = passwordHasher.Hash(password),
            IsAdmin = true,
            MustChangePassword = true,
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            logger.LogInformation(ex, "Bootstrap admin already exists (concurrent seed). Skipping.");
        }
    }

    private async Task ResetAdminPasswordAsync(CancellationToken ct)
    {
        var resetPassword = configuration["ADMIN_RESET_PASSWORD"];
        if (string.IsNullOrEmpty(resetPassword))
            return;

        var targetUsername = configuration["ADMIN_RESET_USERNAME"];
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
            if (admins.Count == 0)
            {
                logger.LogWarning("ADMIN_RESET_PASSWORD is set but no admin user was found.");
                return;
            }

            if (admins.Count > 1)
            {
                logger.LogError(
                    "ADMIN_RESET_PASSWORD is set but multiple admins exist. " +
                    "Set ADMIN_RESET_USERNAME to specify which admin to reset.");
                return;
            }

            admin = admins[0];
        }

        admin.PasswordHash = passwordHasher.Hash(resetPassword);
        admin.MustChangePassword = true;
        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Admin password has been reset via ADMIN_RESET_PASSWORD. Remove the environment variable after logging in.");
    }
}
