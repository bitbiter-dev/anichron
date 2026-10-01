using Anichron.API.Infrastructure;
using Anichron.API.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class OriginalsStorageHealthCheckTests
{
    private const string OriginalsPath = AppDefaults.Storage.OriginalsPath;

    private static HealthCheckContext BuildContext() => new()
    {
        Registration = new HealthCheckRegistration("originalsStorage", _ => null!, default, default)
    };

    [Fact]
    public async Task CheckHealthAsync_DirectoryExists_ReturnsHealthy()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>(),
            new MockFileSystemOptions { CurrentDirectory = "/" });
        fs.Directory.CreateDirectory(OriginalsPath);
        var testee = new OriginalsStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Originals storage directory found.");
    }

    // Degraded, not Unhealthy: the NAS mount being absent stops originals being served on
    // demand, but proxies live on local SSD and every other endpoint keeps working. Reporting
    // Unhealthy would take the whole API out of rotation for a partial loss of function.
    [Fact]
    public async Task CheckHealthAsync_DirectoryDoesNotExist_ReturnsDegraded()
    {
        var fs = new MockFileSystem();
        var testee = new OriginalsStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Originals storage directory not found.");
    }

    // A throwing file system is a different condition from a missing directory — an unreadable
    // or hung mount, not an absent one — so it reports Unhealthy and carries the exception.
    [Fact]
    public async Task CheckHealthAsync_FileSystemThrows_ReturnsUnhealthy()
    {
        var fs = Substitute.For<System.IO.Abstractions.IFileSystem>();
        fs.Directory.Exists(Arg.Any<string?>()).Returns(_ => throw new IOException("mount unreachable"));
        var testee = new OriginalsStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<IOException>();
    }

    // ⭐ The check must read the ORIGINALS path, not the proxy one. Mirroring an existing class
    // makes a copy-paste of the wrong constant the likeliest defect, and every assertion above
    // would still pass with ProxyPath substituted — both directories simply exist or do not.
    [Fact]
    public async Task CheckHealthAsync_ChecksTheOriginalsPathAndNotTheProxyPath()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>(),
            new MockFileSystemOptions { CurrentDirectory = "/" });
        fs.Directory.CreateDirectory(AppDefaults.Storage.ProxyPath);
        var testee = new OriginalsStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded,
            "only the proxy directory exists, so a check reading the originals path must report it missing");
    }
}
