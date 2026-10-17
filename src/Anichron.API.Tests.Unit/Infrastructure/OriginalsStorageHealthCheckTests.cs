using Anichron.API.Infrastructure;
using Anichron.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class OriginalsStorageHealthCheckTests
{
    private readonly IOriginalStore store = Substitute.For<IOriginalStore>();

    private static HealthCheckContext BuildContext() => new()
    {
        Registration = new HealthCheckRegistration("originalsStorage", _ => null!, default, default)
    };

    [Fact]
    public async Task CheckHealthAsync_RootExists_ReturnsHealthy()
    {
        store.RootExists().Returns(true);

        var result = await new OriginalsStorageHealthCheck(store).CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Originals storage directory found.");
    }

    // Degraded, not Unhealthy: the NAS mount being absent stops originals being served on
    // demand, but proxies live on local SSD and every other endpoint keeps working. Reporting
    // Unhealthy would take the whole API out of rotation for a partial loss of function.
    [Fact]
    public async Task CheckHealthAsync_RootMissing_ReturnsDegraded()
    {
        store.RootExists().Returns(false);

        var result = await new OriginalsStorageHealthCheck(store).CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Originals storage directory not found.");
    }

    // A throwing file system is a different condition from a missing directory — an unreadable
    // or hung mount, not an absent one — so it reports Unhealthy and carries the exception.
    [Fact]
    public async Task CheckHealthAsync_StoreThrows_ReturnsUnhealthy()
    {
        store.RootExists().Returns(_ => throw new IOException("mount unreachable"));

        var result = await new OriginalsStorageHealthCheck(store).CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<IOException>();
        result.Description.Should().Be("Originals storage check failed.");
    }
}
