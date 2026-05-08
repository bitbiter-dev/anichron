using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;

namespace Anichron.Infrastructure.Configuration;

public sealed record IniEntry(string Section, string Key, Func<string> DefaultValue);

public sealed class AppIniInitializer(IFileSystem fileSystem)
{
    public void EnsureUpToDate(string iniPath, IReadOnlyList<IniEntry> expectedEntries)
    {
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(iniPath)!);

        if (!fileSystem.File.Exists(iniPath))
        {
            WriteNew(iniPath, expectedEntries);
            return;
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fileSystem.File.ReadAllText(iniPath)));
        var config = new ConfigurationBuilder().AddIniStream(stream).Build();
        var missing = expectedEntries
            .Where(e => config[$"{e.Section}:{e.Key}"] is null)
            .ToList();

        if (missing.Count == 0)
            return;

        AppendMissing(iniPath, missing);
    }

    private void WriteNew(string iniPath, IReadOnlyList<IniEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var section in entries.GroupBy(e => e.Section))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{section.Key}]");
            foreach (var entry in section)
                sb.AppendLine(CultureInfo.InvariantCulture, $"{entry.Key} = {entry.DefaultValue()}");
            sb.AppendLine();
        }

        try
        {
            // FileMode.CreateNew is O_CREAT|O_EXCL — atomic exclusive creation; only one wins if two instances race.
            using var stream = fileSystem.FileStream.New(iniPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(sb.ToString());
        }
        catch (IOException)
        {
            return; // Another instance created the file concurrently — proceed with that file.
        }

        Console.Error.WriteLine(
            $"[WARN] Configuration file created at {iniPath}. Review the values and keep this file secure.");
    }

    private void AppendMissing(string iniPath, IReadOnlyList<IniEntry> missing)
    {
        var sb = new StringBuilder();
        foreach (var section in missing.GroupBy(e => e.Section))
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{section.Key}]");
            foreach (var entry in section)
                sb.AppendLine(CultureInfo.InvariantCulture, $"{entry.Key} = {entry.DefaultValue()}");
        }

        // Not atomic: two processes racing here can both append the same entries.
        // The INI provider tolerates duplicate keys (last value wins), so behavior is correct.
        fileSystem.File.AppendAllText(iniPath, sb.ToString());
        Console.Error.WriteLine($"[INFO] Added missing configuration entries to {iniPath}.");
    }
}
