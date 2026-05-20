using System.Diagnostics;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IProcessLauncher
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
}

internal sealed record ProcessResult(int ExitCode, string StandardError);

internal sealed class SystemProcessLauncher : IProcessLauncher
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        // Start draining pipes immediately — must be consumed to prevent the child process
        // from blocking on a full pipe buffer. Cancellation is signalled only via WaitForExitAsync.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
            await stdoutTask;
            var standardError = await stderrTask;
            return new ProcessResult(process.ExitCode, standardError);
        }
        catch (OperationCanceledException)
        {
            // Kill the process tree so GPU encoder children (e.g. vaenc) are not orphaned.
            process.Kill(entireProcessTree: true);
            // Drain fully before returning — the killed process closes its pipes immediately.
            await Task.WhenAll(stdoutTask, stderrTask);
            throw;
        }
    }
}
