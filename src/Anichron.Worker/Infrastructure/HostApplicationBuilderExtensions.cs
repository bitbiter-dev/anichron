using Anichron.Infrastructure.Configuration;
using System.IO.Abstractions;

namespace Anichron.Worker.Infrastructure;

public static class HostApplicationBuilderExtensions
{
    extension(HostApplicationBuilder builder)
    {
        public HostApplicationBuilder AddAppConfiguration()
        {
            var iniPath = AppIniInitializer.PathUnder(builder.Environment.ContentRootPath);
            new AppIniInitializer(new FileSystem()).EnsureUpToDate(iniPath, WorkerIniDefaults.Entries);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
