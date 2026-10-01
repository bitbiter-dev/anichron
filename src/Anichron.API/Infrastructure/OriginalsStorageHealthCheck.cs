using Anichron.API.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.IO.Abstractions;

namespace Anichron.API.Infrastructure;

// Mirrors ProxyStorageHealthCheck for the originals mount. Both exist because the two stores
// fail independently: proxies are local SSD, originals are the NAS.
//
// ⚠️ Degraded, not Unhealthy, when the directory is missing. A vanished NAS mount stops
// on-demand serving of originals, but proxies and every other endpoint keep working — so
// reporting Unhealthy would pull the whole API out of rotation for a partial loss of function.
// A THROWING file system is a different condition (unreadable or hung mount rather than absent)
// and does report Unhealthy, carrying the exception for diagnosis.
internal sealed class OriginalsStorageHealthCheck(IFileSystem fileSystem) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(
                fileSystem.Directory.Exists(AppDefaults.Storage.OriginalsPath)
                    ? HealthCheckResult.Healthy("Originals storage directory found.")
                    : HealthCheckResult.Degraded("Originals storage directory not found."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Originals storage check failed.", ex));
        }
    }
}
