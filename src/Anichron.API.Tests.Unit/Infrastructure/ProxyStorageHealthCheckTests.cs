using Anichron.API.Infrastructure;
using Anichron.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ProxyStorageHealthCheckTests
{
    private readonly IProxyStore store = Substitute.For<IProxyStore>();

    private static HealthCheckContext BuildContext() => new()
    {
        Registration = new HealthCheckRegistration("proxyStorage", _ => null!, default, default)
    };

    [Fact]
    public async Task CheckHealthAsync_RootExists_ReturnsHealthy()
    {
        store.RootExists().Returns(true);

        var result = await new ProxyStorageHealthCheck(store).CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Proxy storage directory found.");
    }

    [Fact]
    public async Task CheckHealthAsync_RootMissing_ReturnsDegraded()
    {
        store.RootExists().Returns(false);

        var result = await new ProxyStorageHealthCheck(store).CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Proxy storage directory not found.");
    }

    [Fact]
    public async Task CheckHealthAsync_StoreThrows_ReturnsUnhealthy()
    {
        store.RootExists().Returns(_ => throw new IOException("mount unreachable"));

        var result = await new ProxyStorageHealthCheck(store).CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<IOException>();
        result.Description.Should().Be("Proxy storage check failed.");
    }
}
