// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leecharr.Http.REST;

public sealed class RestResourceJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(RestResource).IsAssignableFrom(typeToConvert)
            && !typeToConvert.IsAbstract
            && typeToConvert != typeof(RestResource);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(RestResourceJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

public sealed class RestResourceJsonConverter<T> : JsonConverter<T>
    where T : RestResource
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            supplied.Add(property.Name);
        }

        var deserializeOptions = CreateDeserializeOptions(options);
        var resource = root.Deserialize<T>(deserializeOptions)!;
        resource.SuppliedJsonPropertyNames = supplied;
        return resource;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, CreateDeserializeOptions(options));
    }

    private static JsonSerializerOptions CreateDeserializeOptions(JsonSerializerOptions options)
    {
        var deserializeOptions = new JsonSerializerOptions(options);
        for (var i = deserializeOptions.Converters.Count - 1; i >= 0; i--)
        {
            var converter = deserializeOptions.Converters[i];
            if (converter is RestResourceJsonConverterFactory)
            {
                deserializeOptions.Converters.RemoveAt(i);
                continue;
            }

            var converterType = converter.GetType();
            if (converterType.IsGenericType
                && converterType.GetGenericTypeDefinition() == typeof(RestResourceJsonConverter<>))
            {
                deserializeOptions.Converters.RemoveAt(i);
            }
        }

        return deserializeOptions;
    }
}
