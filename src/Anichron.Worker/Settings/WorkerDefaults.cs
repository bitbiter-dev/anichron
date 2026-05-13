namespace Anichron.Worker.Settings;

internal static class WorkerDefaults
{
    internal static class Migrator
    {
        internal const int MaxAttempts = 10;
        internal const int RetryDelaySeconds = 5;
    }
}
