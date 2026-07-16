using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public class RssRule : ModelBase
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
    public List<int> IndexerIds { get; set; } = new();
}
