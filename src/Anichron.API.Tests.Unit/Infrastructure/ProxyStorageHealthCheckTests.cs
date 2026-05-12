using Anichron.API.Infrastructure;
using Anichron.API.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ProxyStorageHealthCheckTests
{
    private const string ProxyPath = AppDefaults.Storage.ProxyPath;

    private static HealthCheckContext BuildContext() => new()
    {
        Registration = new HealthCheckRegistration("proxyStorage", _ => null!, default, default)
    };

    [Fact]
    public async Task CheckHealthAsync_DirectoryExists_ReturnsHealthy()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>(),
            new MockFileSystemOptions { CurrentDirectory = "/" });
        fs.Directory.CreateDirectory(ProxyPath);
        var testee = new ProxyStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Proxy storage directory found.");
    }

    [Fact]
    public async Task CheckHealthAsync_DirectoryDoesNotExist_ReturnsDegraded()
    {
        var fs = new MockFileSystem();
        var testee = new ProxyStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Proxy storage directory not found.");
    }

    [Fact]
    public async Task CheckHealthAsync_FileSystemThrows_ReturnsUnhealthy()
    {
        var fs = Substitute.For<System.IO.Abstractions.IFileSystem>();
        fs.Directory.Exists(Arg.Any<string?>()).Returns(_ => throw new IOException("disk error"));
        var testee = new ProxyStorageHealthCheck(fs);

        var result = await testee.CheckHealthAsync(BuildContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<IOException>();
    }
}
