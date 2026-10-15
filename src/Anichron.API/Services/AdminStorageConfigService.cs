using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.API.Services;

public interface IAdminStorageConfigService
{
    Task<AuthResult<List<UserStorageConfig>>> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task<AuthResult<UserStorageConfig>> AddAsync(Guid userId, string rootPath, CancellationToken ct);
    Task<AuthResult> DeleteAsync(Guid userId, Guid configId, CancellationToken ct);
}

public sealed class AdminStorageConfigService(
    IUserRepository users,
    IUserStorageConfigRepository storageConfigs,
    IGuidFactory guidFactory,
    IUnitOfWork unitOfWork) : IAdminStorageConfigService
{
    public async Task<AuthResult<List<UserStorageConfig>>> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return AuthResult.Fail<List<UserStorageConfig>>(AuthError.UserNotFound);

        var configs = await storageConfigs.GetByUserIdAsync(userId, ct);
        return AuthResult.Ok(configs);
    }

    public async Task<AuthResult<UserStorageConfig>> AddAsync(Guid userId, string rootPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        var canonicalPath = StoragePath.Canonicalize(rootPath);
        if (canonicalPath is null || !StoragePath.IsWithinOriginals(canonicalPath))
            return AuthResult.Fail<UserStorageConfig>(AuthError.PathInvalid);

        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return AuthResult.Fail<UserStorageConfig>(AuthError.UserNotFound);

        var existingRootPaths = await storageConfigs.GetAllRootPathsAsync(ct);
        if (existingRootPaths.Contains(canonicalPath, StringComparer.Ordinal))
            return AuthResult.Fail<UserStorageConfig>(AuthError.PathAlreadyAssigned);
        if (existingRootPaths.Any(existing => StoragePath.Overlaps(existing, canonicalPath)))
            return AuthResult.Fail<UserStorageConfig>(AuthError.PathOverlaps);

        var config = new UserStorageConfig
        {
            Id = guidFactory.NewGuid(),
            UserId = userId,
            RootPath = canonicalPath,
            IsActive = true,
        };

        storageConfigs.Add(config);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StorageConfigIndexNames.RootPathUnique,
        })
        {
            return AuthResult.Fail<UserStorageConfig>(AuthError.PathAlreadyAssigned);
        }

        return AuthResult.Ok(config);
    }

    public async Task<AuthResult> DeleteAsync(Guid userId, Guid configId, CancellationToken ct)
    {
        var config = await storageConfigs.FindByIdAsync(configId, ct);
        if (config is null || config.UserId != userId)
            return AuthResult.Fail(AuthError.StorageConfigNotFound);

        storageConfigs.Remove(config);
        await unitOfWork.SaveChangesAsync(ct);
        return AuthResult.Ok();
    }
}
