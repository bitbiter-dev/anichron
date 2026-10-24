using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Startup;

public sealed partial class WorkerInitializer(
    IOptions<WorkerSettings> options,
    IServiceScopeFactory scopeFactory,
    WorkerState workerState,
    ILogger<WorkerInitializer> logger) : IHostedService
{
    private readonly WorkerSettings settings = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.User))
        {
            Log.AllUserMode(logger);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var storageConfigRepository = scope.ServiceProvider.GetRequiredService<IUserStorageConfigRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = await userRepository.FindByCredentialAsync(settings.User, cancellationToken)
            ?? throw new InvalidOperationException(
                $"WORKER__USER '{settings.User}' not found in database.");

        if (string.IsNullOrWhiteSpace(settings.RootPath))
        {
            throw new InvalidOperationException(
                $"WORKER__ROOTPATH is required when WORKER__USER is set, e.g. {StoragePath.OriginalsRoot}/<user>.");
        }

        var rootPath = StoragePath.Canonicalize(settings.RootPath)
            ?? throw new InvalidOperationException(
                $"WORKER__ROOTPATH '{settings.RootPath}' must be an absolute path.");
        if (!StoragePath.IsInsideOriginals(rootPath))
        {
            throw new InvalidOperationException(
                $"WORKER__ROOTPATH '{rootPath}' must be a folder inside {StoragePath.OriginalsRoot}: " +
                "use the container path, not the host path.");
        }

        var existing = await storageConfigRepository.FindByRootPathAsync(rootPath, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != user.Id)
            {
                throw new InvalidOperationException(
                    $"Path '{rootPath}' is already assigned to another user.");
            }

            Log.StorageConfigExists(logger, user.Username, rootPath);
            workerState.ResolvedUserId = user.Id;
            return;
        }

        var overlapping = StoragePath.FindOverlap(
            await storageConfigRepository.GetAllRootPathsAsync(cancellationToken), rootPath);
        if (overlapping is not null)
        {
            throw new InvalidOperationException(
                $"Path '{rootPath}' overlaps the existing storage config at '{overlapping}'.");
        }

        storageConfigRepository.Add(new UserStorageConfig
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RootPath = rootPath,
            IsActive = true,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.StorageConfigCreated(logger, user.Username, rootPath);
        workerState.ResolvedUserId = user.Id;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Starting in all-user mode.")]
        public static partial void AllUserMode(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Storage config already exists for '{Username}' at '{Root}'.")]
        public static partial void StorageConfigExists(ILogger logger, string username, string root);

        [LoggerMessage(Level = LogLevel.Information, Message = "Created storage config for '{Username}' at '{Root}'.")]
        public static partial void StorageConfigCreated(ILogger logger, string username, string root);
    }
}
