#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NzbDrone.Core.Automation;

internal static class ScriptJsonElementConverter
{
    public static object? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var trimmed = json.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        return ToScriptValue(doc.RootElement);
    }

    public static object? ToScriptValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => ToScriptArray(element),
            JsonValueKind.Object => ToScriptObject(element),
            _ => element.ToString(),
        };
    }

    private static List<object?> ToScriptArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ToScriptValue(item));
        }

        return list;
    }

    private static Dictionary<string, object?> ToScriptObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ToScriptValue(property.Value);
        }

        return dict;
    }
}
