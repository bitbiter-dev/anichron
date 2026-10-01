using Anichron.API.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Infrastructure;

// ⭐ A health check class that is never registered is dead code that looks like coverage:
// /healthz reports only what AddApiHealthChecks wired up, so forgetting the AddCheck line
// leaves the mount unmonitored while the check and its tests sit there passing. These assert
// the wiring; the per-check classes cover the check logic.
public sealed class ApiHealthCheckRegistrationTests
{
    // ⛔ Asserts the registration's FACTORY resolves to the expected type, not just that the
    // name is present. Comparing names alone would also be satisfied by
    // `.AddCheck<ProxyStorageHealthCheck>("originalsStorage")` — the name there while the
    // originals mount goes unchecked, which is exactly the failure this class claims to catch.
    //
    // ⚠️ The factory is invoked for the ONE named registration only. Activating every
    // registration fails: AddDbContextCheck's factory needs AnichronDbContext at activation
    // time, not when the check later runs, so resolving it here would demand a real DbContext
    // in what is meant to be a wiring test. Measured — it throws
    // "Unable to resolve service for type 'AnichronDbContext'".
    private static void ShouldBeRegistered<TCheck>(string name)
        where TCheck : IHealthCheck
    {
        var services = new ServiceCollection();
        services.AddApiHealthChecks();
        using var provider = services.BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.SingleOrDefault(r => r.Name == name);

        registration.Should().NotBeNull($"'{name}' must be registered in AddApiHealthChecks");
        registration.Factory(provider).Should().BeOfType<TCheck>(
            $"'{name}' must be backed by {typeof(TCheck).Name}");
    }

    [Fact]
    public void AddApiHealthChecks_RegistersTheOriginalsStorageCheck()
        => ShouldBeRegistered<OriginalsStorageHealthCheck>("originalsStorage");

    // Pinned alongside, so a future edit cannot drop one while adding another — and so a
    // copy-paste pointing two names at the same class fails rather than passing quietly.
    [Fact]
    public void AddApiHealthChecks_RegistersTheProxyStorageCheck()
        => ShouldBeRegistered<ProxyStorageHealthCheck>("proxyStorage");

    // Name only: see the note above on why this one's factory is not activated here.
    [Fact]
    public void AddApiHealthChecks_RegistersTheDatabaseCheck()
    {
        var services = new ServiceCollection();
        services.AddApiHealthChecks();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(r => r.Name).Should().Contain("database");
    }
}
