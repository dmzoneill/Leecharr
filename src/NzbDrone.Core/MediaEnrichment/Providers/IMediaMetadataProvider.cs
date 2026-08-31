// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public interface IMediaMetadataProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Version { get; }

    string Description { get; }

    bool IsAvailable { get; }

    MediaMetadataCapabilities Capabilities { get; }

    Task<MediaMetadataHealthCheckResult> ProbeHealthAsync();

    Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null);
}
