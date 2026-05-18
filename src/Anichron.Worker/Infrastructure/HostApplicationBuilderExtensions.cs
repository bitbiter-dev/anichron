using Anichron.Infrastructure.Configuration;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
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
                new IniEntry("Worker", "TokenCleanupIntervalHours", () => "24"),
                new IniEntry("Worker", "ProxyPath", () => "/data/proxies"),
            ]);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }

        public HostApplicationBuilder AddIngestionServices()
        {
            builder.Services.AddSingleton<IFileSystem, FileSystem>();
            builder.Services.AddSingleton<ILivePhotoLinker, LivePhotoLinker>();
            builder.Services.AddSingleton<IFileIngestionPipeline, FileIngestionPipeline>();
            builder.Services.AddIngestionSteps();
            return builder;
        }
    }
}
