using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IVideoProxyGenerator
{
    string FileName { get; }
    ProxyType ProxyType { get; }
    Task TranscodeAsync(string sourceAbsolutePath, string outputAbsolutePath, CancellationToken ct);
}

internal sealed class WebVideoGenerator(IVideoProcessor videoProcessor) : IVideoProxyGenerator
{
    public string FileName => "video.mp4";
    public ProxyType ProxyType => ProxyType.WebVideo;

    public Task TranscodeAsync(string sourceAbsolutePath, string outputAbsolutePath, CancellationToken ct)
        => videoProcessor.TranscodeAsync(sourceAbsolutePath, outputAbsolutePath, ct);
}
