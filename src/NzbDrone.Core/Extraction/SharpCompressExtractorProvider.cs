using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace NzbDrone.Core.Extraction;

public class SharpCompressExtractorProvider : IArchiveExtractorProvider
{
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".lz", ".z"
    };

    public string ProviderId => "SharpCompress";
    public string DisplayName => "SharpCompress (Pure C# .NET)";
    public string Version => typeof(ArchiveFactory).Assembly.GetName().Version?.ToString() ?? "0.50.4";
    public string Description => "Pure managed C# archive extraction engine powered by SharpCompress. Zero native dependencies.";
    public bool IsAvailable => true;

    public ArchiveExtractorCapabilities Capabilities { get; } = new()
    {
        SupportsRar5 = true,
        Supports7z = true,
        SupportsZip = true,
        SupportsTarGz = true,
        SupportsMultiPart = true,
        SupportsPasswordProtected = true,
        SupportsSolidArchives = true,
        SupportsRecoveryVolumes = false
    };

    public SharpCompressExtractorProvider(IDiskProvider diskProvider)
    {
        _diskProvider = diskProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Task<ExtractorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "SharpCompress managed library is operational.",
            DependencyChecks = new List<string> { "SharpCompress .NET assembly: Loaded & Ready" }
        });
    }

    public bool CanExtract(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public async Task<bool> ExtractAsync(string archivePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !_diskProvider.FileExists(archivePath))
        {
            _logger.Warn("Archive file does not exist: {0}", archivePath);
            return false;
        }

        var targetDir = destinationPath;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            targetDir = Path.GetDirectoryName(archivePath) ?? "/tmp";
        }

        _diskProvider.EnsureFolder(targetDir);

        bool ExtractAction()
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Info("SharpCompress extracting '{0}' to '{1}'...", archivePath, targetDir);

                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.OpenArchive(stream);

                var options = new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                };

                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entry.WriteToDirectory(targetDir, options);
                }

                _logger.Info("SharpCompress successfully extracted archive '{0}'.", archivePath);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("Extraction of '{0}' was canceled.", archivePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "SharpCompress failed to extract archive: {0}", archivePath);
                return false;
            }
        }

        return await Task.Run(ExtractAction, cancellationToken);
    }
}
