using Anichron.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
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

    // ==========================================================================
    // GetConnectionString
    // ==========================================================================

    [Fact]
    public void GetConnectionString_AllConfigKeysPresent_ReturnsValidConnectionString()
    {
        var config = BuildConfig(BaseConfig());

        var result = DatabaseConfiguration.GetConnectionString(config);
        var parsed = new NpgsqlConnectionStringBuilder(result);

        Assert.Multiple(() =>
        {
            parsed.Host.Should().Be("db-host");
            parsed.Database.Should().Be("anichron_db");
            parsed.Username.Should().Be("alice");
            parsed.Password.Should().Be("secret");
            parsed.Port.Should().Be(5432);
            parsed.Pooling.Should().BeTrue();
        });
    }

    [Fact]
    public void GetConnectionString_PortMissing_DefaultsTo5432()
    {
        var config = BuildConfig(BaseConfig());

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config));

        parsed.Port.Should().Be(5432);
    }

    [Fact]
    public void GetConnectionString_PortPresent_UsesConfiguredPort()
    {
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:PORT"] = "5433";
        var config = BuildConfig(entries);

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config));

        parsed.Port.Should().Be(5433);
    }

    [Fact]
    public void GetConnectionString_PortNonNumeric_DefaultsTo5432()
    {
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:PORT"] = "xyz";
        var config = BuildConfig(entries);

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config));

        parsed.Port.Should().Be(5432);
    }

    [Fact]
    public void GetConnectionString_MissingHost_ThrowsInvalidOperationException()
    {
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:HOST");
        var config = BuildConfig(entries);

        var act = () => DatabaseConfiguration.GetConnectionString(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Host*");
    }

    [Fact]
    public void GetConnectionString_MissingDatabase_ThrowsInvalidOperationException()
    {
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:DBNAME");
        var config = BuildConfig(entries);

        var act = () => DatabaseConfiguration.GetConnectionString(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Database*");
    }

    [Fact]
    public void GetConnectionString_MissingUsernameAndNoFile_ThrowsInvalidOperationException()
    {
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:USER");
        var config = BuildConfig(entries);

        var act = () => DatabaseConfiguration.GetConnectionString(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*USER*");
    }

    [Fact]
    public void GetConnectionString_UserFileExists_ReadsUsernameFromFile()
    {
        const string filePath = "/run/secrets/db_user";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { filePath, new MockFileData("file_alice") },
        });
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:USER");
        entries["POSTGRES_CONNECTION:USER_FILE"] = filePath;
        var config = BuildConfig(entries);

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config, fs));

        parsed.Username.Should().Be("file_alice");
    }

    [Fact]
    public void GetConnectionString_UserFileDoesNotExist_FallsBackToConfigKey()
    {
        var fs = new MockFileSystem(); // empty — file path won't exist
        var entries = BaseConfig();
        entries["POSTGRES_CONNECTION:USER_FILE"] = "/run/secrets/db_user";
        var config = BuildConfig(entries);

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config, fs));

        parsed.Username.Should().Be("alice");
    }

    [Fact]
    public void GetConnectionString_SecretFileContent_IsTrimmed()
    {
        const string filePath = "/run/secrets/db_user";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { filePath, new MockFileData("  alice\n") },
        });
        var entries = BaseConfig();
        entries.Remove("POSTGRES_CONNECTION:USER");
        entries["POSTGRES_CONNECTION:USER_FILE"] = filePath;
        var config = BuildConfig(entries);

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config, fs));

        parsed.Username.Should().Be("alice");
    }
}
