using Anichron.Core.Domain;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.IO;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>
/// Reads the transforms of a HEIF's primary item (<c>irot</c>, <c>imir</c>) in the order its <c>ipma</c> entry lists them,
/// the order libheif applies them in.
/// </summary>
internal static class HeifTransforms
{
    /// <summary>Each transform as an EXIF orientation, first applied first; empty when the primary item has none.</summary>
    /// <param name="stream">The file, positioned at its start.</param>
    public static IReadOnlyList<int> Read(Stream stream)
    {
        uint? primary = null;
        var properties = new List<int?>();
        var associations = new Dictionary<uint, List<int>>();
        QuickTimeReader.ProcessAtoms(stream, top =>
        {
            if (top.TypeString != "meta")
                return;
            top.Reader.Skip(4);
            QuickTimeReader.ProcessAtoms(stream, meta =>
            {
                if (meta.TypeString == "pitm")
                    primary = ItemId(meta.Reader, FullBoxHeader(meta.Reader).Version);
                Into(meta, "iprp", iprp =>
                {
                    Into(iprp, "ipco", property => properties.Add(Transform(property)));
                    if (iprp.TypeString == "ipma")
                        ReadAssociations(iprp.Reader, associations);
                });
            }, top.BytesLeft);
        });

        return primary is { } item && associations.TryGetValue(item, out var indexes)
            ? [.. indexes.Where(index => index >= 1 && index <= properties.Count).Select(index => properties[index - 1]).OfType<int>()]
            : [];
    }

    private static void Into(AtomCallbackArgs atom, string type, Action<AtomCallbackArgs> children)
    {
        if (atom.TypeString == type)
            QuickTimeReader.ProcessAtoms(atom.Stream, children, atom.BytesLeft);
    }

    private static int? Transform(AtomCallbackArgs property) => property.TypeString switch
    {
        "irot" => ExifOrientation.OfDisplayRotation((property.Reader.GetByte() & 3) * 90),
        "imir" => (property.Reader.GetByte() & 1) == 0 ? ExifOrientation.FlipTopBottom : ExifOrientation.FlipLeftRight,
        _ => null,
    };

    private static void ReadAssociations(SequentialReader reader, Dictionary<uint, List<int>> associations)
    {
        var (version, flags) = FullBoxHeader(reader);
        var entries = reader.GetUInt32();
        for (var entry = 0u; entry < entries; entry++)
        {
            var item = ItemId(reader, version);
            var count = reader.GetByte();
            var indexes = new List<int>();
            for (var association = 0; association < count; association++)
                indexes.Add((flags & 1) == 0 ? reader.GetByte() & 0x7F : reader.GetUInt16() & 0x7FFF);
            associations[item] = indexes;
        }
    }

    private static (int Version, int Flags) FullBoxHeader(SequentialReader reader)
    {
        var version = reader.GetByte();
        reader.Skip(2);
        return (version, reader.GetByte());
    }

    private static uint ItemId(SequentialReader reader, int version) => version == 0 ? reader.GetUInt16() : reader.GetUInt32();
}
