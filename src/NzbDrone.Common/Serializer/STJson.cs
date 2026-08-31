using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer;

public static class STJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string ToJson<T>(this T obj)
    {
        return JsonSerializer.Serialize(obj, Options);
    }

    public static T FromJson<T>(this string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static JsonSerializerOptions GetSerializerSettings()
    {
        return new JsonSerializerOptions(Options);
    }
}
