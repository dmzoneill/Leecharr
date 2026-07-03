using System.Data;
using System.Text.Json;
using Dapper;

namespace NzbDrone.Core.Datastore;

public class EmbeddedDocumentConverter<T> : SqlMapper.TypeHandler<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = JsonSerializer.Serialize(value, Options);
    }

    public override T Parse(object value)
    {
        var json = value as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
