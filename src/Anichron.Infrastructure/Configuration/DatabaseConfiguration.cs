using Microsoft.Extensions.Configuration;
using Npgsql;
using System.IO.Abstractions;

namespace Anichron.Infrastructure.Configuration;

public static class DatabaseConfiguration
{
    private const int DefaultPort = 5432;

    public static string GetConnectionString(IConfiguration configuration, IFileSystem? fileSystem = null)
    {
        fileSystem ??= new FileSystem();
        const string sectionName = "POSTGRES_CONNECTION";
        var section = configuration.GetSection(sectionName);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = section["HOST"] ?? throw new InvalidOperationException("Postgres Host missing"),
            Database = section["DBNAME"] ?? throw new InvalidOperationException("Postgres Database name missing"),
            Port = int.TryParse(section["PORT"], out var p) ? p : DefaultPort,
            Username = GetSecretOrConfig(section, "USER_FILE", "USER", fileSystem),
            Password = GetSecretOrConfig(section, "PASSWORD_FILE", "PASSWORD", fileSystem),
            Pooling = true,
        };

        return builder.ToString();
    }

    private static string GetSecretOrConfig(
        IConfigurationSection section, string fileKey, string configKey, IFileSystem fileSystem)
    {
        var filePath = section[fileKey];
        return !string.IsNullOrEmpty(filePath) && fileSystem.File.Exists(filePath)
            ? fileSystem.File.ReadAllText(filePath).Trim()
            : section[configKey]
                ?? throw new InvalidOperationException(
                    $"Configuration value for {configKey} or secret file {fileKey} not found.");
    }
}
