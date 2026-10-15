namespace Anichron.Infrastructure.Configuration;

public static class ConfigurationList
{
    public static List<T> Parse<T>(string? value, Func<string, T> parse)
        => [.. (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(parse)];
}
