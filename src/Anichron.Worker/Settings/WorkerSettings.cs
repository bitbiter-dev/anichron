namespace Anichron.Worker.Settings;

public sealed record WorkerSettings
{
    public string User { get; init; } = string.Empty;

    public string RootPath { get; init; } = "/data/originals";

    public int CrawlIntervalHours { get; init; } = 4;

    public int MaxConcurrentFiles { get; init; } = 4;

    public double TokenCleanupIntervalHours { get; init; } = 24;

    public string ProxyPath { get; init; } = "/data/proxies";
}
