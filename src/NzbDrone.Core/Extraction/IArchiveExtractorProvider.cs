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
    Task<bool> ExtractAsync(string archivePath, string destinationPath, CancellationToken cancellationToken = default);
    bool CanExtract(string filePath);
}
