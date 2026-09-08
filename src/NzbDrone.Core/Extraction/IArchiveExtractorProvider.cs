// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Extraction;

public interface IArchiveExtractorProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Version { get; }

    string Description { get; }

    bool IsAvailable { get; }

    ArchiveExtractorCapabilities Capabilities { get; }

    Task<ExtractorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default);

    Task<bool> ExtractAsync(
        string archivePath,
        string destinationPath,
        string password = null,
        IReadOnlyList<string> passwordCandidates = null,
        CancellationToken cancellationToken = default);

    bool CanExtract(string filePath);
}
