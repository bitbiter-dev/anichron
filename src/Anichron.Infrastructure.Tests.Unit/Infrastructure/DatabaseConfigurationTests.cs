using Anichron.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Infrastructure.Tests.Unit.Infrastructure;

public sealed class DatabaseConfigurationTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> entries)
        => new ConfigurationBuilder().AddInMemoryCollection(entries).Build();

    private static Dictionary<string, string?> BaseConfig() => new()
    {
        ["POSTGRES_CONNECTION:HOST"] = "db-host",
        ["POSTGRES_CONNECTION:DBNAME"] = "anichron_db",
        ["POSTGRES_CONNECTION:USER"] = "alice",
        ["POSTGRES_CONNECTION:PASSWORD"] = "secret",
    };

    private static DatabaseConnectionSettings Bind(Dictionary<string, string?> entries, MockFileSystem? fileSystem = null)
    {
        var settings = new DatabaseConnectionSettings();
        DatabaseConfiguration.Bind(BuildConfig(entries), fileSystem ?? new MockFileSystem(), settings);
        return settings;
    }

    private static Action Start(Dictionary<string, string?> entries)
        => () =>
        {
            using var provider = new ServiceCollection().AddDatabaseSettings(BuildConfig(entries)).BuildServiceProvider();
            provider.GetRequiredService<IStartupValidator>().Validate();
        };

    // ==========================================================================
    // AddDatabaseSettings
    // ==========================================================================

    [Fact]
    public void AddDatabaseSettings_CompleteConnection_PassesStartupValidation()
        => Start(BaseConfig()).Should().NotThrow();

    [Fact]
    public void AddDatabaseSettings_TwoSettingsMissing_NamesBothAtStart()
    {
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:HOST");
        entries.Remove("POSTGRES_CONNECTION:PASSWORD");

        Start(entries).Should().Throw<OptionsValidationException>().Which.Failures.Should().Equal(
            "Postgres Host missing",
            "Configuration value for PASSWORD or secret file PASSWORD_FILE not found.");
    }

    [Fact]
    public void AddDatabaseSettings_BindsTheConfiguredConnection()
    {
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:PORT"] = "5433";
        using var provider = new ServiceCollection().AddDatabaseSettings(BuildConfig(entries)).BuildServiceProvider();

        provider.GetRequiredService<IOptions<DatabaseConnectionSettings>>().Value.Should().BeEquivalentTo(new
        {
            Host = "db-host",
            Database = "anichron_db",
            Port = 5433,
            Username = "alice",
            Password = "secret",
        });
    }

    // ==========================================================================
    // Bind
    // ==========================================================================

    [Fact]
    public void Bind_PortMissing_DefaultsTo5432()
        => Bind(BaseConfig()).Port.Should().Be(5432);

    [Fact]
    public void Bind_PortPresent_UsesConfiguredPort()
    {
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:PORT"] = "5433";

        Bind(entries).Port.Should().Be(5433);
    }

    [Fact]
    public void Bind_PortNonNumeric_DefaultsTo5432()
    {
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:PORT"] = "xyz";

        Bind(entries).Port.Should().Be(5432);
    }

    [Fact]
    public void Bind_UserFileExists_ReadsUsernameFromFile()
    {
        const string filePath = "/run/secrets/db_user";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { filePath, new MockFileData("file_alice") },
        });
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:USER");
        entries["POSTGRES_CONNECTION:USER_FILE"] = filePath;

        Bind(entries, fs).Username.Should().Be("file_alice");
    }

    [Fact]
    public void Bind_UserFileDoesNotExist_FallsBackToConfigKey()
    {
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:USER_FILE"] = "/run/secrets/db_user";

        Bind(entries).Username.Should().Be("alice");
    }

    [Fact]
    public void Bind_PasswordFileExists_WinsOverTheConfigKey()
    {
        const string filePath = "/run/secrets/db_password";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { filePath, new MockFileData("from-file") },
        });
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:PASSWORD_FILE"] = filePath;

        Bind(entries, fs).Password.Should().Be("from-file");
    }

    [Fact]
    public void Bind_SecretFileContent_IsTrimmed()
    {
        const string filePath = "/run/secrets/db_user";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { filePath, new MockFileData("  alice\n") },
        });
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:USER");
        entries["POSTGRES_CONNECTION:USER_FILE"] = filePath;

        Bind(entries, fs).Username.Should().Be("alice");
    }

    [Fact]
    public void Bind_NothingConfigured_LeavesEverySettingMissingWithoutThrowing()
    {
        var settings = Bind([]);

        settings.MissingSettings().Should().Equal(
            "Postgres Host missing",
            "Postgres Database name missing",
            "Configuration value for USER or secret file USER_FILE not found.",
            "Configuration value for PASSWORD or secret file PASSWORD_FILE not found.");
    }

    [Fact]
    public void Bind_AllConfigKeysPresent_MissesNothing()
        => Bind(BaseConfig()).MissingSettings().Should().BeEmpty();

    [Theory]
    [InlineData("POSTGRES_CONNECTION:HOST", "Postgres Host missing")]
    [InlineData("POSTGRES_CONNECTION:DBNAME", "Postgres Database name missing")]
    [InlineData("POSTGRES_CONNECTION:USER", "Configuration value for USER or secret file USER_FILE not found.")]
    [InlineData("POSTGRES_CONNECTION:PASSWORD", "Configuration value for PASSWORD or secret file PASSWORD_FILE not found.")]
    public void Bind_OneKeyMissing_ReportsOnlyThatSetting(string key, string failure)
    {
        var entries = BaseConfig();
        entries.Remove(key);

        Bind(entries).MissingSettings().Should().Equal(failure);
    }

    [Fact]
    public void ToConnectionString_UsesTheBoundValues()
    {
        var parsed = new NpgsqlConnectionStringBuilder(Bind(BaseConfig()).ToConnectionString());

        parsed.Host.Should().Be("db-host");
        parsed.Database.Should().Be("anichron_db");
        parsed.Username.Should().Be("alice");
        parsed.Password.Should().Be("secret");
        parsed.Port.Should().Be(5432);
        parsed.Pooling.Should().BeTrue();
    }
}
