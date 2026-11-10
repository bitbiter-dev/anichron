namespace Anichron.Worker.Scheduling;

/// <summary>
/// Logs a poll loop's outage once: the first failure as an error with its exception, each following one at debug,
/// and the first success after it as a recovery.
/// </summary>
/// <param name="logger">The poll loop's own logger.</param>
/// <param name="operation">What the loop does, as the start of a sentence.</param>
internal sealed partial class OutageLog(ILogger logger, string operation)
{
    private bool failing;

    public void Succeeded()
    {
        if (!failing)
            return;

        failing = false;
        Log.Recovered(logger, operation);
    }

    public void Failed(Exception ex)
    {
        if (failing)
            Log.StillFailing(logger, operation);
        else
            Log.Failed(logger, operation, ex);
        failing = true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "{Operation} failed; will retry at the next poll.")]
        public static partial void Failed(ILogger logger, string operation, Exception ex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "{Operation} failed again; will retry at the next poll.")]
        public static partial void StillFailing(ILogger logger, string operation);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Operation} works again.")]
        public static partial void Recovered(ILogger logger, string operation);
    }
}
