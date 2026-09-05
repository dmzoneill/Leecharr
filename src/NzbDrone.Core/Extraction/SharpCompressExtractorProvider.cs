// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".lz", ".z", ".001",
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
        SupportsRecoveryVolumes = false,
    };

    public SharpCompressExtractorProvider(IDiskProvider diskProvider)
    {
        this.diskProvider = diskProvider;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<ExtractorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "SharpCompress managed library is operational.",
            DependencyChecks = new List<string> { "SharpCompress .NET assembly: Loaded & Ready" },
        });
    }

    public bool CanExtract(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext))
        {
            return true;
        }

        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".7z.001", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rar.001", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".zip.001", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tar.001", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ExtractAsync(string archivePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !this.diskProvider.FileExists(archivePath))
        {
            this.logger.Warn("Archive file does not exist: {0}", archivePath);
            return false;
        }

        var targetDir = destinationPath;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            targetDir = Path.GetDirectoryName(archivePath) ?? "/tmp";
        }

        this.diskProvider.EnsureFolder(targetDir);

        bool ExtractAction()
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.logger.Info("SharpCompress extracting '{0}' to '{1}'...", archivePath, targetDir);

                using var archive = ArchiveFactory.OpenArchive(archivePath);

                var options = new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true,
                };

                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entry.WriteToDirectory(targetDir, options);
                }

                this.logger.Info("SharpCompress successfully extracted archive '{0}'.", archivePath);
                return true;
            }
            catch (OperationCanceledException)
            {
                this.logger.Warn("Extraction of '{0}' was canceled.", archivePath);
                throw;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "SharpCompress failed to extract archive: {0}", archivePath);
                return false;
            }
        }

        return await Task.Run(ExtractAction, cancellationToken);
    }
}
