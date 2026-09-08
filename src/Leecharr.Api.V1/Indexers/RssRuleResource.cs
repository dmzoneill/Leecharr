using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Indexers;

public class RssRuleResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(1024)]
    public string MustContain { get; set; }

    [StringLength(1024)]
    public string MustNotContain { get; set; }

    [Range(0, int.MaxValue)]
    public int MinSeeders { get; set; } = 1;

    [Range(0, long.MaxValue)]
    public long MinSizeBytes { get; set; }

    [Range(0, long.MaxValue)]
    public long MaxSizeBytes { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxAgeDays { get; set; }

    public bool FreeleechOnly { get; set; }

    [Range(0, int.MaxValue)]
    public int CategoryId { get; set; }

    private List<int> indexerIds = new();

    [JsonConverter(typeof(IntListOrCommaSeparatedConverter))]
    public List<int> IndexerIds
    {
        get => this.indexerIds;
        set => this.indexerIds = value ?? new List<int>();
    }
}
