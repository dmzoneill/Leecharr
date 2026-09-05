// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Indexers;

public class RssRuleResource : RestResource
{
    public string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string MustContain { get; set; }

    public string MustNotContain { get; set; }

    public int MinSeeders { get; set; } = 1;

    public long MinSizeBytes { get; set; }

    public long MaxSizeBytes { get; set; }

    public bool FreeleechOnly { get; set; }

    public int CategoryId { get; set; }

    private List<int> indexerIds = new();

    [JsonConverter(typeof(IntListOrCommaSeparatedConverter))]
    public List<int> IndexerIds
    {
        get => this.indexerIds;
        set => this.indexerIds = value ?? new List<int>();
    }
}
