// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dapper;

namespace NzbDrone.Core.Datastore;

public static class TableMapping
{
    private static readonly ConcurrentDictionary<Type, string> TableNames = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Table), string> InsertSqlCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Table), string> UpdateSqlCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Table), string> DeleteSqlCache = new();

    public static void Register<TModel>(string tableName)
        where TModel : ModelBase
    {
        TableNames[typeof(TModel)] = tableName;
    }

    public static string GetTableName(Type type)
    {
        if (TableNames.TryGetValue(type, out var name))
        {
            return name;
        }

        if (type.Name.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return type.Name;
        }

        return type.Name + "s";
    }

    public static string GetInsertSql<TModel>(string table, TModel model = null)
        where TModel : ModelBase
    {
        return GetInsertSql(typeof(TModel), table);
    }

    public static string GetInsertSql(Type type, string table)
    {
        return InsertSqlCache.GetOrAdd((type, table), static key =>
        {
            var properties = GetWritableProperties(key.Type);
            var columns = string.Join(", ", properties.Select(p => $"\"{p.Name}\""));
            var parameters = string.Join(", ", properties.Select(p => $"@{p.Name}"));

            return $"INSERT INTO \"{key.Table}\" ({columns}) VALUES ({parameters})";
        });
    }

    public static string GetUpdateSql<TModel>(string table, TModel model = null)
        where TModel : ModelBase
    {
        return GetUpdateSql(typeof(TModel), table);
    }

    public static string GetUpdateSql(Type type, string table)
    {
        return UpdateSqlCache.GetOrAdd((type, table), static key =>
        {
            var properties = GetWritableProperties(key.Type);
            var setClauses = string.Join(", ", properties.Select(p => $"\"{p.Name}\" = @{p.Name}"));

            return $"UPDATE \"{key.Table}\" SET {setClauses} WHERE \"Id\" = @Id";
        });
    }

    public static string GetDeleteSql<TModel>(string table)
        where TModel : ModelBase
    {
        return GetDeleteSql(typeof(TModel), table);
    }

    public static string GetDeleteSql(Type type, string table)
    {
        return DeleteSqlCache.GetOrAdd((type, table), static key => $"DELETE FROM \"{key.Table}\" WHERE \"Id\" = @Id");
    }

    public static void ClearCache()
    {
        PropertyCache.Clear();
        InsertSqlCache.Clear();
        UpdateSqlCache.Clear();
        DeleteSqlCache.Clear();
    }

    private static PropertyInfo[] GetWritableProperties(Type type)
    {
        TableRegistration.RegisterTypeHandlers();
        return PropertyCache.GetOrAdd(type, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Id" && p.CanRead && p.CanWrite && IsColumnType(p.PropertyType) && p.GetCustomAttribute<IgnoreAttribute>() == null)
                .ToArray());
    }

    private static bool IsColumnType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(DateTime)
            || underlying == typeof(decimal)
            || underlying == typeof(Guid)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(DateOnly)
            || underlying == typeof(double)
            || underlying == typeof(List<int>)
            || underlying == typeof(List<string>)
            || underlying == typeof(Dictionary<string, string>)
            || SqlMapper.HasTypeHandler(type)
            || SqlMapper.HasTypeHandler(underlying);
    }
}
