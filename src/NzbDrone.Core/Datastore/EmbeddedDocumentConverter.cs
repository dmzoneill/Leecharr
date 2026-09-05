// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Data;
using System.Text.Json;
using Dapper;

namespace NzbDrone.Core.Datastore;

public class EmbeddedDocumentConverter<T> : SqlMapper.TypeHandler<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = JsonSerializer.Serialize(value ?? CreateDefault(), Options);
    }

    public override T Parse(object value)
    {
        var json = value as string;
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDefault();
        }

        return JsonSerializer.Deserialize<T>(json, Options) ?? CreateDefault();
    }

    private static T CreateDefault()
    {
        if (typeof(T).GetConstructor(Type.EmptyTypes) != null)
        {
            return Activator.CreateInstance<T>();
        }

        return default;
    }
}
