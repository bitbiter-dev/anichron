using Anichron.Worker.Ingestion.Proxy;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class SystemProcessLauncherTests
{
    [Fact]
    public async Task RunAsync_BinaryStandardOutput_ReturnsItsBytesUnchangedAsync()
    {
        var result = await new SystemProcessLauncher().RunAsync(
            "/bin/sh", ["-c", @"printf '\211PNG\r\n\032\n\377\000'"], TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0x00);
    }

    [Fact]
    public async Task RunAsync_FailingProcess_ReturnsItsExitCodeAndStandardErrorAsync()
    {
        var result = await new SystemProcessLauncher().RunAsync(
            "/bin/sh", ["-c", "echo broken >&2; exit 3"], TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(3);
        result.StandardError.Should().Be("broken\n");
    }
}
