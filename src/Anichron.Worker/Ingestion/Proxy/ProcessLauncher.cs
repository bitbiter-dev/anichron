namespace Anichron.Worker.Ingestion.Proxy;

internal interface IProcessLauncher
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
}

internal sealed record ProcessResult(int ExitCode, string StandardError);
