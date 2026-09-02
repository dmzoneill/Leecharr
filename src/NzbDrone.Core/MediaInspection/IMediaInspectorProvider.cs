// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaInspection;

public interface IMediaInspectorProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Version { get; }

    string Description { get; }

    bool IsAvailable { get; }

    MediaInspectorCapabilities Capabilities { get; }

    Task<MediaInspectorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default);

    Task<MediaContainerInfo> InspectMediaAsync(string mediaPath, CancellationToken cancellationToken = default);

    MediaContainerInfo InspectFile(string filePath);

    MediaContainerInfo Inspect(Stream stream, string fileName = "");
}
