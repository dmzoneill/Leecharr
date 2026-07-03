using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace NzbDrone.Core.Datastore;

public static class TableMapping
{
    private static readonly ConcurrentDictionary<Type, string> TableNames = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

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

        return type.Name + "s";
    }

    public static string GetInsertSql<TModel>(string table, TModel model)
        where TModel : ModelBase
    {
        var properties = GetWritableProperties(typeof(TModel));
        var columns = string.Join(", ", properties.Select(p => $"\"{p.Name}\""));
        var parameters = string.Join(", ", properties.Select(p => $"@{p.Name}"));

        return $"INSERT INTO \"{table}\" ({columns}) VALUES ({parameters})";
    }

    public static string GetUpdateSql<TModel>(string table, TModel model)
        where TModel : ModelBase
    {
        var properties = GetWritableProperties(typeof(TModel));
        var setClauses = string.Join(", ", properties.Select(p => $"\"{p.Name}\" = @{p.Name}"));

        return $"UPDATE \"{table}\" SET {setClauses} WHERE \"Id\" = @Id";
    }

    private static PropertyInfo[] GetWritableProperties(Type type)
    {
        return PropertyCache.GetOrAdd(type, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Id" && p.CanRead && p.CanWrite)
                .ToArray());
    }
}
