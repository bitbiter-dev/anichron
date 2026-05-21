using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Ingestion.Proxy;

internal interface IVideoProcessor
{
    Task TranscodeAsync(string sourcePath, string outputPath, CancellationToken ct);
}

internal sealed partial class FfmpegVideoProcessor(
    IProcessLauncher processLauncher,
    IOptions<WorkerSettings> options,
    ILogger<FfmpegVideoProcessor> logger) : IVideoProcessor, IDisposable
{
    private static readonly string[] encoderPriority =
    [
        H264Encoder.QuickSync,
        H264Encoder.Nvenc,
        H264Encoder.Amf,
        H264Encoder.Software,
    ];

    private readonly SemaphoreSlim encoderDetectionLock = new(1, 1);
    private string? detectedEncoder;

    public async Task TranscodeAsync(string sourcePath, string outputPath, CancellationToken ct)
    {
        string encoder;
        await encoderDetectionLock.WaitAsync(ct);
        try
        {
            detectedEncoder ??= await DetectEncoderAsync(ct);
            encoder = detectedEncoder;
        }
        finally
        {
            encoderDetectionLock.Release();
        }

        await RunFfmpegAsync(sourcePath, outputPath, encoder, ct);
    }

    private async Task<string> DetectEncoderAsync(CancellationToken ct)
    {
        foreach (var encoder in encoderPriority)
        {
            if (await ProbeEncoderAsync(encoder, ct))
            {
                Log.EncoderSelected(logger, encoder);
                return encoder;
            }

            if (encoder != H264Encoder.Software)
                Log.EncoderUnavailable(logger, encoder);
        }

        return H264Encoder.Software;
    }

    private async Task<bool> ProbeEncoderAsync(string encoder, CancellationToken ct)
    {
        // Encode a tiny synthetic clip to /dev/null — success means the encoder is available.
        // /dev/null is Linux-only; the deployment target is linux/amd64 + linux/arm64 (see Dockerfile).
        string[] arguments =
        [
            "-f", "lavfi",
            "-i", "color=black:s=64x64:r=1:d=0.1",
            "-c:v", encoder,
            "-f", "mp4",
            "/dev/null",
            "-y",
        ];
        var result = await processLauncher.RunAsync(options.Value.FfmpegPath, arguments, ct);
        return result.ExitCode == 0;
    }

    private async Task RunFfmpegAsync(string sourcePath, string outputPath, string encoder, CancellationToken ct)
    {
        // Scale to max height, encode H.264 High/4.0 with yuv420p (universal browser compat),
        // target bitrate, AAC audio, faststart for progressive web loading.
        string[] arguments =
        [
            "-i", sourcePath,
            "-vf", $"scale=-2:'min({options.Value.VideoMaxHeight},ih)'",
            "-c:v", encoder,
            "-pix_fmt", "yuv420p",
            "-profile:v", "high",
            "-level:v", "4.0",
            "-b:v", $"{options.Value.VideoBitrateKbps}k",
            "-c:a", "aac",
            "-b:a", "128k",
            "-movflags", "+faststart",
            "-y",
            outputPath,
        ];
        var result = await processLauncher.RunAsync(options.Value.FfmpegPath, arguments, ct);
        if (result.ExitCode != 0)
            throw new FfmpegException(options.Value.FfmpegPath, string.Join(" ", arguments), result.ExitCode, result.StandardError);
    }

    public void Dispose() => encoderDetectionLock.Dispose();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Selected H.264 encoder: {Encoder}.")]
        public static partial void EncoderSelected(ILogger logger, string encoder);

        [LoggerMessage(Level = LogLevel.Debug, Message = "H.264 encoder {Encoder} is unavailable; trying next.")]
        public static partial void EncoderUnavailable(ILogger logger, string encoder);
    }
}

internal static class H264Encoder
{
    internal const string QuickSync = "h264_qsv";
    internal const string Nvenc = "h264_nvenc";
    internal const string Amf = "h264_amf";
    internal const string Software = "libx264";
}

public sealed class FfmpegException : Exception
{
    public int ExitCode { get; }
    public string Stderr { get; } = string.Empty;

    public FfmpegException() { }
    public FfmpegException(string message) : base(message) { }
    public FfmpegException(string message, Exception innerException) : base(message, innerException) { }

    public FfmpegException(string ffmpegPath, string arguments, int exitCode, string stderr)
        : base($"FFmpeg exited with code {exitCode}. Args: {ffmpegPath} {arguments}\nStderr: {stderr}")
    {
        ExitCode = exitCode;
        Stderr = stderr;
    }
}
