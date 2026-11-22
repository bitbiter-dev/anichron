using Anichron.Worker.Settings;
using ImageMagick;
using ImageMagick.Formats;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.CompactPreviews;

internal interface ICompactPreviewEncoder
{
    /// <summary>Re-encodes a preview as a compact JPEG XL, shrunk to the configured long edge, never enlarged.</summary>
    /// <param name="preview">The encoded preview.</param>
    byte[] Encode(byte[] preview);
}

internal sealed class MagickCompactPreviewEncoder(IOptions<WorkerSettings> options) : ICompactPreviewEncoder
{
    public byte[] Encode(byte[] preview)
    {
        var settings = options.Value;
        var edge = (uint)settings.CompactPreviewLongEdge;
        using var image = new MagickImage(preview);
        image.Resize(new MagickGeometry(edge, edge) { Greater = true });
        image.Settings.SetDefines(new JxlWriteDefines
        {
            Quality = settings.CompactPreviewQuality,
            Effort = settings.CompactPreviewEffort,
        });
        return image.ToByteArray(MagickFormat.Jxl);
    }
}
