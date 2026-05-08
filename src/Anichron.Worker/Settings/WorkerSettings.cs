namespace Anichron.Worker.Settings;

public sealed record WorkerSettings
{
    public string User { get; init; } = string.Empty;

    public string RootPath { get; init; } = "/data/originals";

    public int CrawlIntervalHours { get; init; } = 4;

    public int MaxConcurrentFiles { get; init; } = 4;
}
