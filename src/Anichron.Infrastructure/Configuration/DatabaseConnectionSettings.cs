using Npgsql;

namespace Anichron.Infrastructure.Configuration;

public sealed class DatabaseConnectionSettings
{
    public string? Host { get; set; }
    public string? Database { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    public IReadOnlyList<string> MissingSettings()
    {
        List<string> missing = [];
        if (Host is null)
            missing.Add("Postgres Host missing");
        if (Database is null)
            missing.Add("Postgres Database name missing");
        if (Username is null)
            missing.Add(SecretMissing("USER"));
        if (Password is null)
            missing.Add(SecretMissing("PASSWORD"));
        return missing;
    }

    public string ToConnectionString()
        => new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Database = Database,
            Port = Port,
            Username = Username,
            Password = Password,
            Pooling = true,
        }.ToString();

    private static string SecretMissing(string configKey)
        => $"Configuration value for {configKey} or secret file {configKey}_FILE not found.";
}
