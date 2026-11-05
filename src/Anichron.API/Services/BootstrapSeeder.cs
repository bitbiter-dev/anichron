using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.API.Services;

public interface IBootstrapSeeder
{
    Task SeedAsync(CancellationToken ct);
}

public sealed partial class BootstrapSeeder(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IGuidFactory guidFactory,
    IPasswordHasher passwordHasher,
    IRegistrationValidator validator,
    IConfiguration configuration,
    ILogger<BootstrapSeeder> logger) : IBootstrapSeeder
{
    private const string passwordSetting = "BOOTSTRAP_ADMIN_PASSWORD";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await users.AnyAsync(ct))
            return;

        var rawUsername = configuration["BOOTSTRAP_ADMIN_USERNAME"];
        var username = string.IsNullOrWhiteSpace(rawUsername) ? AppDefaults.Startup.AdminDefaultUsername : rawUsername;
        var password = configuration[passwordSetting];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException($"{passwordSetting} must be set to create the bootstrap admin.");
        await validator.EnsureMeetsPolicyAsync(passwordSetting, password, ct);

        Log.CreatingBootstrapAdmin(logger);

        var user = new User
        {
            Id = guidFactory.NewGuid(),
            Username = IdentityNormalizer.Normalize(username),
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
            Log.BootstrapAdminAlreadyExists(logger, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "No users exist. Creating bootstrap admin.")]
        public static partial void CreatingBootstrapAdmin(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap admin already exists (concurrent seed). Skipping.")]
        public static partial void BootstrapAdminAlreadyExists(ILogger logger, Exception ex);
    }
}
