// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Common;

namespace NzbDrone.Core.Extraction;

public class SevenZipExtractorProvider : IArchiveExtractorProvider
{
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".rar", ".zip", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".lz", ".z", ".iso", ".cab", ".arj", ".lzh", ".wim",
    };

    public string ProviderId => "SevenZip";

    public string DisplayName => "7-Zip / p7zip (CLI / Native)";

    public string Version => "24.09 (7-Zip / p7zip)";

    public string Description => "High-performance native 7-Zip CLI extractor supporting 7z, RAR5, multi-part, and solid archives.";

    public bool IsAvailable => FindBinary() != null;

    public ArchiveExtractorCapabilities Capabilities { get; } = new()
    {
        SupportsRar5 = true,
        Supports7z = true,
        SupportsZip = true,
        SupportsTarGz = true,
        SupportsMultiPart = true,
        SupportsPasswordProtected = true,
        SupportsSolidArchives = true,
        SupportsRecoveryVolumes = true,
    };

    public SevenZipExtractorProvider(IDiskProvider diskProvider)
    {
        this.diskProvider = diskProvider;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<ExtractorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        var binary = FindBinary();
        if (binary != null)
        {
            return Task.FromResult(new ExtractorHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"7-Zip executable found at {binary}.",
                DependencyChecks = new List<string> { $"7-Zip binary: {binary}" },
            });
        }

        return Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "7-Zip / p7zip executable not found on PATH or standard locations.",
            Warnings = new List<string> { "Install p7zip-full or 7-Zip, or set SEVENZIP_PATH environment variable." },
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
        if (string.IsNullOrWhiteSpace(archivePath) || !this.diskProvider.FileExists(archivePath))
        {
            this.logger.Warn("Archive file does not exist: {0}", archivePath);
            return false;
        }

        var binary = FindBinary();
        if (binary == null)
        {
            this.logger.Error("7-Zip binary not found on host. Cannot perform native extraction of '{0}'.", archivePath);
            return false;
        }

        var targetDir = destinationPath;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            targetDir = Path.GetDirectoryName(archivePath) ?? "/tmp";
        }

        this.diskProvider.EnsureFolder(targetDir);

        try
        {
            this.logger.Info("7-Zip extracting '{0}' to '{1}' using '{2}'...", archivePath, targetDir, binary);

            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("x");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add($"-o{targetDir}");
            startInfo.ArgumentList.Add(archivePath);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                this.logger.Info("7-Zip successfully extracted archive '{0}'.", archivePath);
                return true;
            }

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            this.logger.Warn("7-Zip extraction finished with exit code {0}: {1}", process.ExitCode, stderr);
            return false;
        }
        catch (OperationCanceledException)
        {
            this.logger.Warn("7-Zip extraction of '{0}' was canceled.", archivePath);
            throw;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "7-Zip failed to extract archive: {0}", archivePath);
            return false;
        }
    }

    private static string FindBinary()
    {
        return CliProcessDiscovery.FindExecutable("7z", "SEVENZIP_PATH", new[] { "/usr/bin/7z", "/usr/local/bin/7z" })
            ?? CliProcessDiscovery.FindExecutable("7za", "SEVENZIP_PATH")
            ?? CliProcessDiscovery.FindExecutable("7zr", "SEVENZIP_PATH")
            ?? CliProcessDiscovery.FindExecutable("p7zip", "SEVENZIP_PATH");
    }
}
