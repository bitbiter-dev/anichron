using System.Buffers.Binary;

namespace Anichron.Worker.Ingestion.Proxy;

internal static class ImageCompleteness
{
    private const byte markerPrefix = 0xFF;
    private const byte endOfImage = 0xD9;

    public static void EnsureHeif(ReadOnlySpan<byte> heif)
    {
        while (!heif.IsEmpty)
        {
            if (!BinaryPrimitives.TryReadUInt32BigEndian(heif, out var compactSize))
                throw HeifEndsEarly();

            var size = compactSize == 1 ? LargeSize(heif) : compactSize;
            if (size == 0)
                return;
            if (size > (ulong)heif.Length)
                throw HeifEndsEarly();

            heif = heif[(int)size..];
        }
    }

    private static ulong LargeSize(ReadOnlySpan<byte> box)
        => box.Length >= 16 ? BinaryPrimitives.ReadUInt64BigEndian(box[8..]) : throw HeifEndsEarly();

    public static void EnsureJpeg(ReadOnlySpan<byte> jpeg)
    {
        var rest = jpeg[2..];
        while (true)
        {
            var prefix = rest.IndexOf(markerPrefix);
            if (prefix < 0)
                throw JpegEndsEarly();

            rest = rest[prefix..];
            var code = rest.IndexOfAnyExcept(markerPrefix);
            if (code < 0)
                throw JpegEndsEarly();

            var marker = rest[code];
            rest = rest[(code + 1)..];
            if (marker == endOfImage)
                return;
            if (marker is 0x00 or 0x01 or (>= 0xD0 and <= 0xD7))
                continue;
            if (!BinaryPrimitives.TryReadUInt16BigEndian(rest, out var length))
                throw JpegEndsEarly();

            rest = rest[Math.Min(length, rest.Length)..];
        }
    }

    private static IncompleteImageException HeifEndsEarly()
        => new("The image data is incomplete: the file ends inside a HEIF box.");

    private static IncompleteImageException JpegEndsEarly()
        => new("The image data is incomplete: the file ends before the JPEG's end-of-image marker.");
}
