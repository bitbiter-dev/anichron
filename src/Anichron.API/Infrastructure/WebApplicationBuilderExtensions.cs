using Anichron.Infrastructure.Configuration;
using System.IO.Abstractions;

namespace Anichron.API.Infrastructure;

public static class WebApplicationBuilderExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddAppConfiguration()
        {
            var iniPath = AppIniInitializer.PathUnder(builder.Environment.ContentRootPath);
            new AppIniInitializer(new FileSystem()).EnsureUpToDate(iniPath, ApiIniDefaults.Entries);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
