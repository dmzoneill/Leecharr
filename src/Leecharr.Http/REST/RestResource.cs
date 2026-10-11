// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Leecharr.Http.REST;

public abstract class RestResource
{
    [JsonIgnore]
    public HashSet<string> SuppliedJsonPropertyNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Id { get; set; }

    [JsonIgnore]
    public virtual string ResourceName
    {
        get
        {
            var typeName = this.GetType().Name;

            if (typeName.EndsWith("Resource", System.StringComparison.Ordinal))
            {
                typeName = typeName[..^8];
            }

            return typeName.ToLowerInvariant();
        }
    }
}
