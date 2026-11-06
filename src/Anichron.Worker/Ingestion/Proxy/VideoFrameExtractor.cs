using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IVideoFrameExtractor
{
    Task<Image<Rgb24>> ExtractAsync(string sourcePath, CancellationToken ct);
}

internal sealed class FfmpegFrameExtractor(IProcessLauncher processLauncher, IOptions<WorkerSettings> options) : IVideoFrameExtractor
{
    public async Task<Image<Rgb24>> ExtractAsync(string sourcePath, CancellationToken ct)
    {
        var frame = await ReadFrameAsync(["-ss", "1", "-i", sourcePath], ct);
        if (frame.Length == 0)
            frame = await ReadFrameAsync(["-i", sourcePath], ct);
        if (frame.Length == 0)
            throw new FfmpegException($"FFmpeg read no frame from {sourcePath}.");
        return Image.Load<Rgb24>(frame);
    }

    private Task<byte[]> ReadFrameAsync(string[] input, CancellationToken ct)
        => processLauncher.RunFfmpegQuietlyAsync(
            options.Value.FfmpegPath,
            [.. input, "-frames:v", "1", "-pix_fmt", "rgb24", "-c:v", "png", "-f", "image2pipe", "-"],
            ct);
}
