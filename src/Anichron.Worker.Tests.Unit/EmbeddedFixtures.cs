namespace Anichron.Worker.Tests.Unit;

internal static class EmbeddedFixtures
{
    public static byte[] Read(string name)
    {
        using var stream = typeof(EmbeddedFixtures).Assembly.GetManifestResourceStream($"Fixtures.{name}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
