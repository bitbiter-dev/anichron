using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.IO.Abstractions;

namespace Anichron.Infrastructure.Configuration;

public static class AppConfigurationExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddAppConfiguration(IReadOnlyList<IniEntry> iniEntries)
        {
            var iniPath = AppIniInitializer.PathUnder(builder.Environment.ContentRootPath);
            new AppIniInitializer(new FileSystem()).EnsureUpToDate(iniPath, iniEntries);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
