using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;

namespace Anichron.Core.Work;

public static class WorkJson
{
    public static JsonSerializerOptions Options
        => new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    public static string Serialize<TPayload>(TPayload payload)
        where TPayload : IWorkPayload
        => JsonSerializer.Serialize(payload, Options);

    public static TPayload Deserialize<TPayload>(string json)
        where TPayload : IWorkPayload
        => JsonSerializer.Deserialize<TPayload>(json, Options)
            ?? throw new JsonException($"The payload of a {TPayload.Kind} item is null.");

    public static string? SerializeResult(object? result)
        => result is null ? null : JsonSerializer.Serialize(result, result.GetType(), Options);
}
