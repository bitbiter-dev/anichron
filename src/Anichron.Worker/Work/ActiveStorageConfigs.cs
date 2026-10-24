using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.Worker.Work;

/// <summary>The result of a per-config item whose storage config is missing or inactive.</summary>
/// <param name="Reason">Why nothing was done.</param>
public sealed record StorageConfigSkipped(string Reason)
{
    public static StorageConfigSkipped MissingOrInactive => new("The storage config is missing or inactive.");
}

internal sealed partial class ActiveStorageConfigs(
    IUserStorageConfigRepository configs,
    ILogger<ActiveStorageConfigs> logger)
{
    /// <summary>The config, or null (logged) when it is missing or inactive and the item is skipped.</summary>
    /// <param name="kind">The kind of the item asking.</param>
    /// <param name="storageConfigId">The item's storage config.</param>
    /// <param name="ct">Cancels the lookup.</param>
    public async Task<UserStorageConfig?> FindAsync(WorkKind kind, Guid storageConfigId, CancellationToken ct)
    {
        if (await configs.FindByIdAsync(storageConfigId, ct) is { IsActive: true } config)
            return config;

        Log.Skipped(logger, kind, storageConfigId);
        return null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Skipping {Kind} of storage config {Id}: it is missing or inactive.")]
        public static partial void Skipped(ILogger logger, WorkKind kind, Guid id);
    }
}
