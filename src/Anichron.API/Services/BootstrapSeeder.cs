using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.API.Services;

public interface IBootstrapSeeder
{
    Task SeedAsync(CancellationToken ct);
}

public sealed class BootstrapSeeder(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IGuidFactory guidFactory,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<BootstrapSeeder> logger) : IBootstrapSeeder
{
    public async Task SeedAsync(CancellationToken ct)
    {
        if (await users.AnyAsync(ct))
            return;

        var username = configuration["BOOTSTRAP_ADMIN_USERNAME"] ?? AppDefaults.Startup.AdminDefaultUsername;
        var password = configuration["BOOTSTRAP_ADMIN_PASSWORD"] ?? AppDefaults.Startup.AdminDefaultPassword;

        logger.LogWarning("No users exist. Creating bootstrap admin.");

        var user = new User
        {
            Id = guidFactory.NewGuid(),
            Username = username.Trim().ToLowerInvariant(),
            Email = AppDefaults.Startup.AdminDefaultMail,
            PasswordHash = passwordHasher.Hash(password),
            IsAdmin = true,
            MustChangePassword = true,
        };

        users.Add(user);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            logger.LogInformation(ex, "Bootstrap admin already exists (concurrent seed). Skipping.");
        }
    }
}
