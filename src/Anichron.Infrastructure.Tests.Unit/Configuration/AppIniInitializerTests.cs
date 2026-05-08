using Anichron.Infrastructure.Configuration;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Infrastructure.Tests.Unit.Configuration;

public sealed class AppIniInitializerTests
{
    private const string IniPath = "/app/config/app.ini";

    private static readonly IReadOnlyList<IniEntry> DefaultEntries =
    [
        new IniEntry("Section", "KeyA", () => "valueA"),
        new IniEntry("Section", "KeyB", () => "valueB"),
        new IniEntry("Other", "KeyC", () => "valueC"),
    ];

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
    // Create new file
    // ==========================================================================

    [Fact]
    public void EnsureUpToDate_FileDoesNotExist_CreatesFileWithAllEntries()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        testee.EnsureUpToDate(IniPath, DefaultEntries);
        var content = fs.File.ReadAllText(IniPath);

        Assert.Multiple(() =>
        {
            fs.FileExists(IniPath).Should().BeTrue();
            content.Should().Contain("[Section]");
            content.Should().Contain("KeyA = valueA");
            content.Should().Contain("KeyB = valueB");
            content.Should().Contain("[Other]");
            content.Should().Contain("KeyC = valueC");
        });
    }

    [Fact]
    public void EnsureUpToDate_FileDoesNotExist_CreatesParentDirectory()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        testee.EnsureUpToDate(IniPath, DefaultEntries);

        fs.Directory.Exists("/app/config").Should().BeTrue();
    }

    [Fact]
    public void EnsureUpToDate_FileDoesNotExist_PrintsWarningToStderr()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);

        var stderr = CaptureStderr(() => testee.EnsureUpToDate(IniPath, DefaultEntries));

        Assert.Multiple(() =>
        {
            stderr.Should().Contain("[WARN]");
            stderr.Should().Contain(IniPath);
        });
    }

    [Fact]
    public void EnsureUpToDate_FileDoesNotExist_DefaultValueFactoryCalledOnce()
    {
        var fs = new MockFileSystem();
        var testee = new AppIniInitializer(fs);
        var callCount = 0;
        IReadOnlyList<IniEntry> entries = [new IniEntry("S", "K", () => { callCount++; return "v"; })];

        testee.EnsureUpToDate(IniPath, entries);

        callCount.Should().Be(1);
    }

    // ==========================================================================
    // File already up-to-date
    // ==========================================================================

    [Fact]
    public void EnsureUpToDate_AllEntriesPresent_PreservesExistingContent()
    {
        const string existing = "[Section]\r\nKeyA = old\r\nKeyB = old\r\n[Other]\r\nKeyC = old\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);

        testee.EnsureUpToDate(IniPath, DefaultEntries);

        fs.File.ReadAllText(IniPath).Should().Be(existing);
    }

    [Fact]
    public void EnsureUpToDate_AllEntriesPresent_DoesNotPrintToStderr()
    {
        const string existing = "[Section]\r\nKeyA = old\r\nKeyB = old\r\n[Other]\r\nKeyC = old\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);

        var stderr = CaptureStderr(() => testee.EnsureUpToDate(IniPath, DefaultEntries));

        stderr.Should().BeEmpty();
    }

    [Fact]
    public void EnsureUpToDate_AllEntriesPresent_DefaultValueFactoryNeverCalled()
    {
        const string existing = "[Section]\r\nKeyA = old\r\nKeyB = old\r\n[Other]\r\nKeyC = old\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);
        var factoryCalled = false;
        IReadOnlyList<IniEntry> entries =
        [
            new IniEntry("Section", "KeyA", () => { factoryCalled = true; return "x"; }),
            new IniEntry("Section", "KeyB", () => { factoryCalled = true; return "x"; }),
            new IniEntry("Other", "KeyC", () => { factoryCalled = true; return "x"; }),
        ];

        testee.EnsureUpToDate(IniPath, entries);

        factoryCalled.Should().BeFalse();
    }

    // ==========================================================================
    // Append missing entries
    // ==========================================================================

    [Fact]
    public void EnsureUpToDate_SomeEntriesMissing_AppendsMissingEntries()
    {
        const string existing = "[Section]\r\nKeyA = old\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);

        testee.EnsureUpToDate(IniPath, DefaultEntries);
        var content = fs.File.ReadAllText(IniPath);

        Assert.Multiple(() =>
        {
            content.Should().Contain("KeyA = old");
            content.Should().Contain("KeyB = valueB");
            content.Should().Contain("KeyC = valueC");
        });
    }

    [Fact]
    public void EnsureUpToDate_SomeEntriesMissing_PrintsInfoToStderr()
    {
        const string existing = "[Section]\r\nKeyA = old\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);

        var stderr = CaptureStderr(() => testee.EnsureUpToDate(IniPath, DefaultEntries));

        stderr.Should().Contain("[INFO]");
    }

    [Fact]
    public void EnsureUpToDate_SomeEntriesMissing_ExistingEntryFactoryNotCalled()
    {
        const string existing = "[Section]\r\nKeyA = present\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);
        var existingFactoryCalled = false;
        IReadOnlyList<IniEntry> entries =
        [
            new IniEntry("Section", "KeyA", () => { existingFactoryCalled = true; return "x"; }),
            new IniEntry("Section", "KeyB", () => "new"),
        ];

        testee.EnsureUpToDate(IniPath, entries);

        existingFactoryCalled.Should().BeFalse();
    }

    // ==========================================================================
    // Section-key matching is case-insensitive
    // ==========================================================================

    [Fact]
    public void EnsureUpToDate_ExistingKeyCaseDiffers_TreatedAsPresent()
    {
        const string existing = "[SECTION]\r\nKEYA = old\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { IniPath, new MockFileData(existing) },
        });
        var testee = new AppIniInitializer(fs);
        IReadOnlyList<IniEntry> entries = [new IniEntry("Section", "KeyA", () => "new")];

        testee.EnsureUpToDate(IniPath, entries);

        fs.File.ReadAllText(IniPath).Should().Contain("KEYA = old").And.NotContain("new");
    }

    // ==========================================================================
    // JWT secret uniqueness (regression for random factory)
    // ==========================================================================

    [Fact]
    public void EnsureUpToDate_TwoSeparateCalls_ProduceDifferentSecrets()
    {
        var fs1 = new MockFileSystem();
        var fs2 = new MockFileSystem();
        IReadOnlyList<IniEntry> entries =
        [
            new IniEntry("Jwt", "Secret", () => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64))),
        ];

        new AppIniInitializer(fs1).EnsureUpToDate(IniPath, entries);
        new AppIniInitializer(fs2).EnsureUpToDate(IniPath, entries);

        var secret1 = ExtractValue(fs1.File.ReadAllText(IniPath), "Secret");
        var secret2 = ExtractValue(fs2.File.ReadAllText(IniPath), "Secret");

        secret1.Should().NotBe(secret2);
    }

    [Fact]
    public void EnsureUpToDate_GeneratedSecret_IsValidBase64WithSufficientLength()
    {
        var fs = new MockFileSystem();
        IReadOnlyList<IniEntry> entries =
        [
            new IniEntry("Jwt", "Secret", () => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64))),
        ];

        new AppIniInitializer(fs).EnsureUpToDate(IniPath, entries);

        var secret = ExtractValue(fs.File.ReadAllText(IniPath), "Secret");
        var decoded = Convert.FromBase64String(secret);

        Assert.Multiple(() =>
        {
            secret.Should().NotBeEmpty();
            decoded.Length.Should().BeGreaterThanOrEqualTo(32);
        });
    }

    private static string ExtractValue(string content, string key)
    {
        var line = content.Split('\n').First(l => l.TrimStart().StartsWith($"{key} =", StringComparison.Ordinal));
        return line.Split('=', 2)[1].Trim();
    }
}
