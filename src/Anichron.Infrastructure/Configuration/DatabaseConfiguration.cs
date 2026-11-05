using Anichron.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;

namespace Anichron.Infrastructure.Configuration;

public static class DatabaseConfiguration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDatabaseSettings(IConfiguration configuration)
        {
            services.AddOptions<DatabaseConnectionSettings>()
                .Configure(settings => Bind(configuration, new FileSystem(), settings))
                .ValidateOnStart();
            return services.AddSingleton<IValidateOptions<DatabaseConnectionSettings>, DatabaseSettingsValidator>();
        }
    }

    internal static void Bind(IConfiguration configuration, IFileSystem fileSystem, DatabaseConnectionSettings settings)
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
