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
