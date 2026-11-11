namespace Anichron.Worker.Ingestion.Proxy;

internal static class FfmpegLaunch
{
    public static async Task<byte[]> RunFfmpegQuietlyAsync(
        this IProcessLauncher processLauncher, string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        string[] quiet = ["-hide_banner", "-loglevel", "error", .. arguments];
        var result = await processLauncher.RunAsync(ffmpegPath, quiet, ct);
        if (result.ExitCode != 0)
            throw new FfmpegException(ffmpegPath, string.Join(" ", quiet), result.ExitCode, result.StandardError);
        return result.StandardOutput;
    }
}

public sealed class FfmpegException : Exception
{
    public int ExitCode { get; }
    public string Stderr { get; } = string.Empty;

    public FfmpegException() { }
    public FfmpegException(string message) : base(message) { }
    public FfmpegException(string message, Exception innerException) : base(message, innerException) { }

    public FfmpegException(string ffmpegPath, string arguments, int exitCode, string stderr)
        : base(MessageFor(ffmpegPath, arguments, exitCode, stderr))
    {
        ExitCode = exitCode;
        Stderr = stderr;
    }

    private static string MessageFor(string ffmpegPath, string arguments, int exitCode, string stderr)
    {
        var command = $"FFmpeg exited with code {exitCode}: {ffmpegPath} {arguments}";
        var error = stderr.Trim();
        return error.Length == 0 ? command : $"{error}\n{command}";
    }
}
