// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class MediaMetadataHealthCheckResult
{
    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();
}
