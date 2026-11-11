namespace Anichron.Worker.Ingestion.Proxy;

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
