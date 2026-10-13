namespace Anichron.API.Infrastructure;

internal static class ConfigurationList
{
    internal static List<T> Parse<T>(string? value, Func<string, T> parse)
        => [.. (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(parse)];
}
