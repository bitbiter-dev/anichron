using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IVideoProxyGenerator
{
    string FileName { get; }
    ProxyType ProxyType { get; }
    Task TranscodeAsync(string sourceAbsolutePath, string outputAbsolutePath, CancellationToken ct);
}

internal sealed class Video720PGenerator(IVideoProcessor videoProcessor) : IVideoProxyGenerator
{
    public string FileName => "video_720p.mp4";
    public ProxyType ProxyType => ProxyType.WebVideo;

    public Task TranscodeAsync(string sourceAbsolutePath, string outputAbsolutePath, CancellationToken ct)
        => videoProcessor.TranscodeAsync(sourceAbsolutePath, outputAbsolutePath, ct);
}
