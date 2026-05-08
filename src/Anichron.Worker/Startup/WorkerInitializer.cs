using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Startup;

public sealed partial class WorkerInitializer(
    IOptions<WorkerSettings> options,
    IServiceScopeFactory scopeFactory,
    WorkerState workerState,
    ILogger<WorkerInitializer> logger) : IHostedService
{
    private readonly WorkerSettings _settings = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.User))
        {
            Log.AllUserMode(logger);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var storageConfigRepository = scope.ServiceProvider.GetRequiredService<IUserStorageConfigRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var credential = _settings.User.Trim().ToLowerInvariant();
        var user = await userRepository.FindByCredentialAsync(credential, cancellationToken)
            ?? throw new InvalidOperationException(
                $"WORKER__USER '{_settings.User}' not found in database.");

        var existing = await storageConfigRepository.FindByRootPathAsync(_settings.RootPath, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != user.Id)
            {
                throw new InvalidOperationException(
                    $"Path '{_settings.RootPath}' is already assigned to another user.");
            }

            Log.StorageConfigExists(logger, user.Username, _settings.RootPath);
            workerState.ResolvedUserId = user.Id;
            return;
        }

        storageConfigRepository.Add(new UserStorageConfig
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RootPath = _settings.RootPath,
            IsActive = true,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.StorageConfigCreated(logger, user.Username, _settings.RootPath);
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
