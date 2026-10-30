using Anichron.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using System.IO.Abstractions;

namespace Anichron.Infrastructure.Configuration;

public static class DatabaseConfiguration
{
    public static string GetConnectionString(IConfiguration configuration, IFileSystem fileSystem)
    {
        var settings = new DatabaseConnectionSettings();
        Bind(configuration, fileSystem, settings);

        var missing = settings.MissingSettings();
        return missing.Count == 0 ? settings.ToConnectionString() : throw new InvalidOperationException(missing[0]);
    }

    public static void Bind(IConfiguration configuration, IFileSystem fileSystem, DatabaseConnectionSettings settings)
    {
        var section = configuration.GetSection("POSTGRES_CONNECTION");

        settings.Host = section["HOST"];
        settings.Database = section["DBNAME"];
        settings.Port = int.TryParse(section["PORT"], out var port) ? port : PostgresConstants.DefaultPort;
        settings.Username = SecretOrConfig(section, "USER_FILE", "USER", fileSystem);
        settings.Password = SecretOrConfig(section, "PASSWORD_FILE", "PASSWORD", fileSystem);
    }

    private static string? SecretOrConfig(
        IConfigurationSection section, string fileKey, string configKey, IFileSystem fileSystem)
    {
        var filePath = section[fileKey];
        return !string.IsNullOrEmpty(filePath) && fileSystem.File.Exists(filePath)
            ? fileSystem.File.ReadAllText(filePath).Trim()
            : section[configKey];
    }
}
