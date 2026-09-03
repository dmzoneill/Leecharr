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

public class UnrarExtractorProvider : IArchiveExtractorProvider
{
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rar", ".cbr", ".r00", ".r01", ".r02", ".r03", ".part01.rar", ".part1.rar",
    };

    public string ProviderId => "Unrar";

    public string DisplayName => "RARLAB UnRAR (Official Native)";

    public string Version => "7.01 (RARLAB)";

    public string Description => "Official RARLAB UnRAR native extractor with full RAR5 and recovery volume reconstruction support.";

    public bool IsAvailable => FindBinary() != null;

    public ArchiveExtractorCapabilities Capabilities { get; } = new()
    {
        SupportsRar5 = true,
        Supports7z = false,
        SupportsZip = false,
        SupportsTarGz = false,
        SupportsMultiPart = true,
        SupportsPasswordProtected = true,
        SupportsSolidArchives = true,
        SupportsRecoveryVolumes = true,
    };

    public UnrarExtractorProvider(IDiskProvider diskProvider)
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
                StatusMessage = $"RARLAB UnRAR executable found at {binary}.",
                DependencyChecks = new List<string> { $"UnRAR binary: {binary}" },
            });
        }

        return Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "UnRAR executable not found on PATH or standard locations.",
            Warnings = new List<string> { "Install unrar or set UNRAR_PATH environment variable." },
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

        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        return fileName.EndsWith(".part01.rar", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".part1.rar", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
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
            this.logger.Error("UnRAR binary not found on host. Cannot perform native extraction of '{0}'.", archivePath);
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
            var normalizedDest = targetDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? targetDir
                : targetDir + Path.DirectorySeparatorChar;

            this.logger.Info("UnRAR extracting '{0}' to '{1}' using '{2}'...", archivePath, normalizedDest, binary);

            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("x");
            startInfo.ArgumentList.Add("-o+");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(archivePath);
            startInfo.ArgumentList.Add(normalizedDest);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            await process.WaitForExitAsync(cancellationToken);

            // UnRAR exit codes: 0 = Success, 1 = Non-fatal error / Warning (processed with warnings)
            if (process.ExitCode == 0 || process.ExitCode == 1)
            {
                this.logger.Info("UnRAR successfully extracted archive '{0}' (Exit code {1}).", archivePath, process.ExitCode);
                return true;
            }

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            this.logger.Warn("UnRAR extraction finished with error exit code {0}: {1}", process.ExitCode, stderr);
            return false;
        }
        catch (OperationCanceledException)
        {
            this.logger.Warn("UnRAR extraction of '{0}' was canceled.", archivePath);
            throw;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "UnRAR failed to extract archive: {0}", archivePath);
            return false;
        }
    }

    private static string FindBinary()
    {
        return CliProcessDiscovery.FindExecutable("unrar", "UNRAR_PATH", new[] { "/usr/bin/unrar", "/usr/local/bin/unrar" })
            ?? CliProcessDiscovery.FindExecutable("unrar-nonfree", "UNRAR_PATH");
    }
}
