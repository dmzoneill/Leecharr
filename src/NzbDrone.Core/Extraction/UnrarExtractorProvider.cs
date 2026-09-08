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
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Extraction;

public class UnrarExtractorProvider : IArchiveExtractorProvider
{
    private readonly IDiskProvider diskProvider;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly TimeSpan? baseTimeout;
    private readonly int minutesPerGb;
    private readonly Logger logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rar", ".cbr", ".r00", ".r01", ".r02", ".r03", ".part01.rar", ".part1.rar", ".001",
    };

    public string ProviderId => "Unrar";

    public string DisplayName => "RARLAB UnRAR (Official Native)";

    public string Version => "7.01 (RARLAB)";

    public string Description => "Official RARLAB UnRAR native extractor with full RAR5 and recovery volume reconstruction support.";

    public bool IsAvailable => FindBinary() != null;

    public TimeSpan BaseTimeout => this.baseTimeout ?? ArchiveTimeoutCalculator.DefaultBaseTimeout;

    public int MinutesPerGigabyte => this.minutesPerGb;

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

    public UnrarExtractorProvider(
        IDiskProvider diskProvider,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        TimeSpan? baseTimeout = null,
        int minutesPerGb = ArchiveTimeoutCalculator.DefaultMinutesPerGigabyte)
    {
        this.diskProvider = diskProvider;
        this.configService = configService;
        this.configFileProvider = configFileProvider;

        var configuredTimeoutMinutes = (configService != null && configService.ArchiveExtractionTimeoutMinutes > 0)
            ? configService.ArchiveExtractionTimeoutMinutes
            : (configFileProvider != null && configFileProvider.ArchiveExtractionTimeoutMinutes > 0
                ? configFileProvider.ArchiveExtractionTimeoutMinutes
                : 0);

        this.baseTimeout = baseTimeout ?? (configuredTimeoutMinutes > 0 ? TimeSpan.FromMinutes(configuredTimeoutMinutes) : (TimeSpan?)null);
        this.minutesPerGb = minutesPerGb > 0 ? minutesPerGb : ArchiveTimeoutCalculator.DefaultMinutesPerGigabyte;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public TimeSpan CalculateTimeout(string archivePath)
    {
        return ArchiveTimeoutCalculator.CalculateDynamicTimeout(archivePath, this.diskProvider, this.baseTimeout, this.minutesPerGb);
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
            || fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rar.001", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ExtractAsync(
        string archivePath,
        string destinationPath,
        string password = null,
        IReadOnlyList<string> passwordCandidates = null,
        CancellationToken cancellationToken = default)
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
            var dir = Path.GetDirectoryName(archivePath);
            targetDir = !string.IsNullOrWhiteSpace(dir)
                ? dir
                : (!string.IsNullOrWhiteSpace(this.configService?.ExtractorTempDir)
                    ? this.configService.ExtractorTempDir
                    : (!string.IsNullOrWhiteSpace(this.configFileProvider?.ExtractorTempDir)
                        ? this.configFileProvider.ExtractorTempDir
                        : (!string.IsNullOrWhiteSpace(this.configService?.IncompleteDownloadDir)
                            ? this.configService.IncompleteDownloadDir
                            : Path.GetTempPath())));
        }

        this.diskProvider.EnsureFolder(targetDir);

        var passwordsToTry = BuildPasswordCandidateList(password, passwordCandidates);

        foreach (var candidatePassword in passwordsToTry)
        {
            Process process = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedDest = targetDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? targetDir
                    : targetDir + Path.DirectorySeparatorChar;

                var timeout = this.CalculateTimeout(archivePath);
                this.logger.Info("UnRAR extracting '{0}' (timeout: {1:N0}m) to '{2}' using '{3}'...", archivePath, timeout.TotalMinutes, normalizedDest, binary);

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
                startInfo.ArgumentList.Add("-inul");
                if (!string.IsNullOrEmpty(candidatePassword))
                {
                    startInfo.ArgumentList.Add($"-p{candidatePassword}");
                }
                else
                {
                    startInfo.ArgumentList.Add("-p-");
                }

                startInfo.ArgumentList.Add(archivePath);
                startInfo.ArgumentList.Add(normalizedDest);

                process = new Process { StartInfo = startInfo };
                process.Start();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

                try
                {
                    await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch
                    {
                    }

                    this.logger.Warn("UnRAR extraction of '{0}' was canceled.", archivePath);
                    throw;
                }

                // UnRAR exit codes: 0 = Success, 1 = Non-fatal error / Warning (processed with warnings)
                if (process.ExitCode == 0 || process.ExitCode == 1)
                {
                    this.logger.Info("UnRAR successfully extracted archive '{0}' (Exit code {1}).", archivePath, process.ExitCode);
                    return true;
                }

                var stderr = await stderrTask;
                if (passwordsToTry.Count > 1)
                {
                    this.logger.Debug("UnRAR extraction attempt failed with exit code {0}: {1}", process.ExitCode, stderr);
                }
                else
                {
                    this.logger.Warn("UnRAR extraction finished with error exit code {0}: {1}", process.ExitCode, stderr);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "UnRAR failed to extract archive: {0}", archivePath);
                return false;
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch
                    {
                    }

                    process.Dispose();
                }
            }
        }

        return false;
    }

    private static List<string> BuildPasswordCandidateList(string password, IReadOnlyList<string> passwordCandidates)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(password))
        {
            candidates.Add(password);
        }

        if (passwordCandidates != null)
        {
            foreach (var candidate in passwordCandidates)
            {
                if (!string.IsNullOrEmpty(candidate) && !candidates.Contains(candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (candidates.Count == 0)
        {
            candidates.Add(null);
        }

        return candidates;
    }

    private static string FindBinary()
    {
        return CliProcessDiscovery.FindExecutable("unrar", "UNRAR_PATH", new[] { "/usr/bin/unrar", "/usr/local/bin/unrar" })
            ?? CliProcessDiscovery.FindExecutable("unrar-nonfree", "UNRAR_PATH");
    }
}
