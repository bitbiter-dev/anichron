using System.Text.Json;

namespace Anichron.Architecture.Tests.Unit;

internal sealed record ArchitectureException(string Rule, string Type, int Issue, string Reason);

internal static class ArchitectureExceptions
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ArchitectureException> All
        => JsonSerializer.Deserialize<List<ArchitectureException>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "architecture-exceptions.json")),
            Options) ?? [];

    public static IReadOnlySet<string> TypesFor(string rule)
        => All.Where(exception => exception.Rule == rule).Select(exception => exception.Type).ToHashSet();
}
