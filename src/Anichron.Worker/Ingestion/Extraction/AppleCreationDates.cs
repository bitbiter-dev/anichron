using MetadataExtractor.Formats.QuickTime;
using System.Text;

namespace Anichron.Worker.Ingestion.Extraction;

/// <summary>Reads the raw value of <c>com.apple.quicktime.creationdate</c> from <c>moov</c> → <c>meta</c>, with its offset.</summary>
internal static class AppleCreationDates
{
    private const string creationDateKey = "com.apple.quicktime.creationdate";

    /// <summary>The raw creation date, or null when the file records none or its atoms end early.</summary>
    /// <param name="stream">The file, positioned at its start.</param>
    public static string? Read(Stream stream)
    {
        var keys = new List<string>();
        string? value = null;
        QuickTimeReader.ProcessAtoms(stream, top => Into(top, "moov", moov => Into(moov, "meta", meta =>
        {
            if (meta.TypeString == "keys")
            {
                meta.Reader.Skip(8);
                QuickTimeReader.ProcessAtoms(stream, key => keys.Add(Utf8(key)), meta.BytesLeft);
            }

            var index = keys.IndexOf(creationDateKey) + 1;
            Into(meta, "ilst", item =>
            {
                if (item.Type == index)
                    QuickTimeReader.ProcessAtoms(stream, data => value = data.TypeString == "data" ? DataValue(data) : value, item.BytesLeft);
            });
        })));
        return value;
    }

    private static void Into(AtomCallbackArgs atom, string type, Action<AtomCallbackArgs> children)
    {
        if (atom.TypeString == type)
            QuickTimeReader.ProcessAtoms(atom.Stream, children, atom.BytesLeft);
    }

    private static string DataValue(AtomCallbackArgs data)
    {
        data.Reader.Skip(8);
        return Utf8(data);
    }

    private static string Utf8(AtomCallbackArgs atom) => Encoding.UTF8.GetString(atom.Reader.GetBytes((int)atom.BytesLeft));
}
