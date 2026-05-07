using Anichron.API.Infrastructure;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class AppIniInitializerTests
{
    private const string IniPath = "/app/config/app.ini";

    // Redirects Console.Error for the duration of the action and returns whatever was written.
    private static string CaptureStderr(Action action)
    {
        using var writer = new StringWriter();
        var original = Console.Error;
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }

    // ==========================================================================
    // EnsureExists
    // ==========================================================================

    [Fact]
    public void EnsureExists_FileDoesNotExist_CreatesFileWithExpectedIniContent()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        testee.EnsureExists(IniPath);
        var content = fs.File.ReadAllText(IniPath);

        Assert.Multiple(() =>
        {
            fs.FileExists(IniPath).Should().BeTrue();
            content.Should().Contain("[Jwt]");
            content.Should().Contain("Secret = ");
            content.Should().Contain("Issuer = anichron-api");
            content.Should().Contain("Audience = anichron-client");
            content.Should().Contain("[Cors]");
            content.Should().Contain("AllowedOrigins =");
        });
    }

    [Fact]
    public void EnsureExists_FileDoesNotExist_PrintsWarningToStderr()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        var stderr = CaptureStderr(() => testee.EnsureExists(IniPath));

        stderr.Should().Contain("[WARN]");
        stderr.Should().Contain(IniPath);
    }

    [Fact]
    public void EnsureExists_FileDoesNotExist_CreatesParentDirectory()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        testee.EnsureExists(IniPath);

        fs.Directory.Exists("/app/config").Should().BeTrue();
    }

    [Fact]
    public void EnsureExists_FileAlreadyExists_PreservesExistingContent()
    {
        const string original = "original content";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(original) },
        });
        var testee = new AppIniInitializer(fs);

        testee.EnsureExists(IniPath);

        fs.File.ReadAllText(IniPath).Should().Be(original);
    }

    [Fact]
    public void EnsureExists_FileAlreadyExists_DoesNotPrintToStderr()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData("existing") },
        });
        var testee = new AppIniInitializer(fs);

        var stderr = CaptureStderr(() => testee.EnsureExists(IniPath));

        stderr.Should().BeEmpty();
    }

    [Fact]
    public void EnsureExists_FileDoesNotExist_GeneratedSecretIsValidBase64WithSufficientLength()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        testee.EnsureExists(IniPath);
        var secret = ExtractSecret(fs.File.ReadAllText(IniPath));
        var decoded = Convert.FromBase64String(secret);

        Assert.Multiple(() =>
        {
            secret.Should().NotBeEmpty();
            decoded.Length.Should().BeGreaterThanOrEqualTo(32);
        });
    }

    [Fact]
    public void EnsureExists_EachCall_GeneratesUniqueSecret()
    {
        var fs1 = new MockFileSystem();
        var fs2 = new MockFileSystem();

        new AppIniInitializer(fs1).EnsureExists(IniPath);
        new AppIniInitializer(fs2).EnsureExists(IniPath);

        var secret1 = ExtractSecret(fs1.File.ReadAllText(IniPath));
        var secret2 = ExtractSecret(fs2.File.ReadAllText(IniPath));

        secret1.Should().NotBe(secret2);
    }

    private static string ExtractSecret(string content)
    {
        var line = content.Split('\n').First(l => l.TrimStart().StartsWith("Secret =", StringComparison.Ordinal));
        return line.Split('=', 2)[1].Trim();
    }
}
