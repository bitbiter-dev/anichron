using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.API.Services;

public interface IAdminStorageConfigService
{
    Task<List<UserStorageConfig>?> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task<ServiceResult<UserStorageConfig, IAddStorageConfigError>> AddAsync(Guid userId, string rootPath, CancellationToken ct);
    Task<bool> DeleteAsync(Guid userId, Guid configId, CancellationToken ct);
}

public sealed class AdminStorageConfigService(
    IUserRepository users,
    IUserStorageConfigRepository storageConfigs,
    IGuidFactory guidFactory,
    IUnitOfWork unitOfWork) : IAdminStorageConfigService
{
    public async Task<List<UserStorageConfig>?> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return null;

        return await storageConfigs.GetByUserIdAsync(userId, ct);
    }

    public async Task<ServiceResult<UserStorageConfig, IAddStorageConfigError>> AddAsync(Guid userId, string rootPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        var canonicalPath = StoragePath.Canonicalize(rootPath);
        if (canonicalPath is null || !StoragePath.IsInsideOriginals(canonicalPath))
            return new PathInvalid();

        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return new UserNotFound();

        var existingRootPaths = await storageConfigs.GetAllRootPathsAsync(ct);
        if (existingRootPaths.Contains(canonicalPath, StringComparer.Ordinal))
            return new PathAlreadyAssigned();
        if (StoragePath.FindOverlap(existingRootPaths, canonicalPath) is not null)
            return new PathOverlaps();

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
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(StorageConfigIndexNames.RootPathUnique))
        {
            return new PathAlreadyAssigned();
        }

        return config;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid configId, CancellationToken ct)
    {
        var config = await storageConfigs.FindByIdAsync(configId, ct);
        if (config is null || config.UserId != userId)
            return false;

        storageConfigs.Remove(config);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
