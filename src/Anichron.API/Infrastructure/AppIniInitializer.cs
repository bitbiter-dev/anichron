using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Anichron.API.Infrastructure;

internal sealed class AppIniInitializer(IFileSystem fileSystem)
{
    internal void EnsureExists(string iniPath)
    {
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(iniPath)!);

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var content = $"""
            [Jwt]
            Secret = {secret}
            Issuer = anichron-api
            Audience = anichron-client

            [Cors]
            AllowedOrigins =

            """;

        try
        {
            // FileMode.CreateNew is O_CREAT|O_EXCL — atomic exclusive creation. If two instances race, only one succeeds.
            using var stream = fileSystem.FileStream.New(iniPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
        catch (IOException)
        {
            return;
        }

        Console.Error.WriteLine(
            $"[WARN] App configuration created at {iniPath}. " +
            "A JWT signing secret has been generated. Keep this file secure.");
    }
}
