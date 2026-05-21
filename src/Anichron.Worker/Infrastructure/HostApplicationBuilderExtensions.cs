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
                new IniEntry("Worker", "TokenCleanupIntervalHours", () => "24"),
                new IniEntry("Worker", "ProxyPath", () => "/data/proxies"),
                new IniEntry("Worker", "ThumbnailMaxWidth", () => "300"),
                new IniEntry("Worker", "ThumbnailJpegQuality", () => "75"),
                new IniEntry("Worker", "PreviewMaxWidth", () => "1920"),
                new IniEntry("Worker", "PreviewJpegQuality", () => "85"),
                new IniEntry("Worker", "BlurhashSampleWidth", () => "64"),
                new IniEntry("Worker", "FfmpegPath",          () => "ffmpeg"),
                new IniEntry("Worker", "VideoMaxHeight",      () => "720"),
                new IniEntry("Worker", "VideoBitrateKbps",    () => "2000"),
            ]);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
