using Anichron.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anichron.API.Infrastructure;

internal sealed class ProxyStorageHealthCheck(IProxyStore store) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(
                store.RootExists()
                    ? HealthCheckResult.Healthy("Proxy storage directory found.")
                    : HealthCheckResult.Degraded("Proxy storage directory not found."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Proxy storage check failed.", ex));
        }
    }
}
