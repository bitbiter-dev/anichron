using Anichron.API.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.IO.Abstractions;

namespace Anichron.API.Infrastructure;

internal sealed class ProxyStorageHealthCheck(IFileSystem fileSystem) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(
                fileSystem.Directory.Exists(AppDefaults.Storage.ProxyPath)
                    ? HealthCheckResult.Healthy("Proxy storage directory found.")
                    : HealthCheckResult.Degraded("Proxy storage directory not found."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Proxy storage check failed.", ex));
        }
    }
}
