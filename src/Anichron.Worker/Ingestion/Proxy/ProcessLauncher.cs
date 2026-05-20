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
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        try
        {
            // Read stderr without a CT — we must drain the pipe regardless of cancellation.
            // Cancellation is signalled only via WaitForExitAsync.
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(ct);
            var standardError = await stderrTask;
            return new ProcessResult(process.ExitCode, standardError);
        }
        catch (OperationCanceledException)
        {
            // Kill the process tree so GPU encoder children (e.g. vaenc) are not orphaned.
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
