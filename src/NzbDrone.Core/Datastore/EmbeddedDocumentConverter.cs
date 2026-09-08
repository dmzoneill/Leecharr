// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Data;
using System.Text.Json;
using Dapper;
using NLog;

namespace NzbDrone.Core.Datastore;

public class EmbeddedDocumentConverter<T> : SqlMapper.TypeHandler<T>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        if (parameter == null)
        {
            return;
        }

        if (value == null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        parameter.Value = JsonSerializer.Serialize(value, Options);
    }

    public override T Parse(object value)
    {
        if (value == null || value is DBNull)
        {
            return CreateDefault();
        }

        var json = value as string;
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? CreateDefault();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to deserialize JSON for type {0}: {1}", typeof(T).Name, json);
            return CreateDefault();
        }
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
