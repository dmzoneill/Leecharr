// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Config;

internal static class ConfigResourceDictionaryBuilder
{
    public static Dictionary<string, object> ToDictionary<TResource>(TResource resource)
        where TResource : RestResource
    {
        var dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var supplied = resource.SuppliedJsonPropertyNames;
        var saveAllProperties = supplied == null || supplied.Count == 0;

        foreach (var property in typeof(TResource).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name is "Id" or "ResourceName" or nameof(RestResource.SuppliedJsonPropertyNames))
            {
                continue;
            }

            if (!saveAllProperties && !IsPropertySupplied(property, supplied))
            {
                continue;
            }

            dictionary[property.Name] = property.GetValue(resource, null);
        }

        return dictionary;
    }

    private static bool IsPropertySupplied(PropertyInfo property, HashSet<string> supplied)
    {
        var jsonName = GetJsonPropertyName(property);
        if (supplied.Contains(jsonName) || supplied.Contains(property.Name))
        {
            return !string.Equals(jsonName, "id", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (attribute != null && !string.IsNullOrEmpty(attribute.Name))
        {
            return attribute.Name;
        }

        return JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    }
}
