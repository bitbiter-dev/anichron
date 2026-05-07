namespace Anichron.Worker;

/// <summary>
/// Configuration for a Worker instance. All values are read from environment variables
/// or appsettings.json under the "Worker" section.
/// </summary>
public sealed record WorkerSettings
{
    /// <summary>
    /// Email or username of the user this Worker belongs to. Required.
    /// Set via WORKER__USER environment variable.
    /// </summary>
    public string User { get; init; } = string.Empty;

    /// <summary>
    /// Root path inside the container to scan for media. Defaults to /data/originals.
    /// Mount the user's NAS folder to this path in docker-compose.
    /// Set via WORKER__ROOT environment variable.
    /// </summary>
    public string Root { get; init; } = "/data/originals";

    /// <summary>
    /// How often to run a full crawl, in hours. Defaults to 4.
    /// Set via WORKER__CRAWL_INTERVAL_HOURS environment variable.
    /// </summary>
    public int CrawlIntervalHours { get; init; } = 4;

    /// <summary>
    /// Maximum number of files to process concurrently. Defaults to 4.
    /// Set via WORKER__MAX_CONCURRENT_FILES environment variable.
    /// </summary>
    public int MaxConcurrentFiles { get; init; } = 4;
}
