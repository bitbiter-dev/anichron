using Anichron.Infrastructure.Configuration;
using System.IO.Abstractions;

namespace Anichron.Worker.Infrastructure;

public static class HostApplicationBuilderExtensions
{
    extension(HostApplicationBuilder builder)
    {
        public HostApplicationBuilder AddAppConfiguration()
        {
            var iniPath = Path.Combine(builder.Environment.ContentRootPath, "configuration", "app.ini");
            new AppIniInitializer(new FileSystem()).EnsureUpToDate(iniPath,
            [
                new IniEntry("Worker", "User", () => string.Empty),
                new IniEntry("Worker", "RootPath", () => "/data/originals"),
                new IniEntry("Worker", "CrawlIntervalHours", () => "4"),
                new IniEntry("Worker", "MaxConcurrentFiles", () => "4"),
            ]);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
