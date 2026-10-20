namespace Anichron.Tests.Integration;

public sealed class PostgresServerTests
{
    [Fact]
    public void Image_IsThePostgresImageTheComposeFileDeploys()
    {
        var compose = File.ReadAllLines(Path.Combine(SourceRoot(), "docker-compose.yml"));

        compose.Select(line => line.Trim())
            .Should().ContainSingle(line => line.StartsWith("image: postgres:", StringComparison.Ordinal))
            .Which.Should().Be($"image: {PostgresServer.Image}");
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Anichron.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Anichron.slnx not found above the test output.");
    }
}
