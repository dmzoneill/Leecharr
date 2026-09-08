// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public interface ICustomScriptService
{
    Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null);
}

public class CustomScriptService : ICustomScriptService
{
    private readonly IMediaEnrichmentService mediaEnrichmentService;
    private readonly TimeSpan scriptTimeout;
    private readonly TimeSpan streamDrainTimeout;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CustomScriptService(
        IMediaEnrichmentService mediaEnrichmentService = null,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        TimeSpan? scriptTimeout = null,
        TimeSpan? streamDrainTimeout = null)
    {
        this.mediaEnrichmentService = mediaEnrichmentService;
        var timeoutSec = (configService != null && configService.CustomScriptTimeoutSeconds > 0)
            ? configService.CustomScriptTimeoutSeconds
            : (configFileProvider != null && configFileProvider.CustomScriptTimeoutSeconds > 0
                ? configFileProvider.CustomScriptTimeoutSeconds
                : 60);

        this.scriptTimeout = scriptTimeout ?? TimeSpan.FromSeconds(timeoutSec);
        this.streamDrainTimeout = streamDrainTimeout ?? TimeSpan.FromSeconds(3);
    }

    public TimeSpan ScriptTimeout => this.scriptTimeout;

    internal static (string FileName, string Arguments) ResolveInterpreter(string scriptPath, string arguments)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        var args = arguments ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            switch (ext)
            {
                case ".bat":
                case ".cmd":
                    return ("cmd.exe", $"/c \"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".py":
                case ".pyw":
                    return ("python", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".ps1":
                    return ("powershell.exe", $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                default:
                    return (scriptPath, args);
            }
        }
        else
        {
            switch (ext)
            {
                case ".sh":
                    return ("/bin/sh", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".bash":
                    return ("/bin/bash", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".py":
                case ".pyw":
                    return ("python3", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                default:
                    return (scriptPath, args);
            }
        }
    }

    internal static void SanitizeEnvironment(System.Collections.Specialized.StringDictionary environmentVariables)
    {
        var keysToRemove = new List<string>();
        foreach (string key in environmentVariables.Keys)
        {
            if (IsSensitiveEnvironmentVariable(key))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            environmentVariables.Remove(key);
        }
    }

    internal static bool IsSensitiveEnvironmentVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.StartsWith("LEECHARR_TORRENT_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("LEECHARR_MEDIA_", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("LEECHARR_EVENT_TYPE", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("TR_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("TORRENT_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var upper = key.ToUpperInvariant();

        if (upper.StartsWith("LEECHARR__") ||
            upper.StartsWith("DATABASE_") ||
            upper.StartsWith("POSTGRES_") ||
            upper.StartsWith("DB_") ||
            upper.StartsWith("REDIS_") ||
            upper.StartsWith("SECRET_") ||
            upper.StartsWith("API_KEY") ||
            upper.StartsWith("PROXY_"))
        {
            return true;
        }

        var sensitiveKeywords = new[]
        {
            "PASSWORD", "PASSWD", "SECRET", "API_KEY", "APIKEY", "TOKEN", "CREDENTIAL", "AUTH",
            "CONNECTIONSTRING", "PRIVATE_KEY",
        };

        return sensitiveKeywords.Any(k => upper.Contains(k));
    }

    internal static Dictionary<string, string> BuildEnvironmentVariables(string eventType, Torrent torrent, TorrentMediaMetadata meta = null)
    {
        var env = new Dictionary<string, string>
        {
            ["LEECHARR_EVENT_TYPE"] = eventType ?? string.Empty,
        };

        if (torrent != null)
        {
            env["TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            env["TORRENT_NAME"] = torrent.Name ?? string.Empty;
            env["TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            env["TORRENT_CATEGORY"] = torrent.Category ?? string.Empty;
            env["TORRENT_PATH"] = torrent.SavePath ?? string.Empty;
            env["TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            env["TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            env["TORRENT_STATUS"] = torrent.Status.ToString();

            env["LEECHARR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            env["LEECHARR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            env["LEECHARR_TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            env["LEECHARR_TORRENT_CATEGORY"] = torrent.Category ?? string.Empty;
            env["LEECHARR_TORRENT_PATH"] = torrent.SavePath ?? string.Empty;
            env["LEECHARR_TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            env["LEECHARR_TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            env["LEECHARR_TORRENT_STATUS"] = torrent.Status.ToString();

            // Transmission compatibility environment variables
            env["TR_TORRENT_DIR"] = torrent.SavePath ?? string.Empty;
            env["TR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            env["TR_TORRENT_HASH"] = torrent.InfoHash ?? string.Empty;
            env["TR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            env["TR_TIME_LOCALTIME"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            env["TR_APP_VERSION"] = "4.0.0";

            if (meta != null)
            {
                env["LEECHARR_MEDIA_TITLE"] = meta.Title ?? string.Empty;
                env["LEECHARR_MEDIA_YEAR"] = meta.Year > 0 ? meta.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;
                env["LEECHARR_MEDIA_OVERVIEW"] = meta.Overview ?? string.Empty;
                env["LEECHARR_MEDIA_GENRES"] = meta.Genres ?? string.Empty;
                env["LEECHARR_MEDIA_RATING"] = meta.Rating > 0 ? meta.Rating.ToString("F1", CultureInfo.InvariantCulture) : string.Empty;
                env["LEECHARR_MEDIA_IMDB_ID"] = meta.ImdbId ?? string.Empty;
            }
        }

        return env;
    }

    public static (string ScriptPath, string Arguments) ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return (string.Empty, null);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                string path = null;
                string arguments = null;

                var pathProps = new[] { "path", "Path", "scriptPath", "ScriptPath", "script", "Script", "filename", "Filename" };
                foreach (var prop in pathProps)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        path = val.GetString() ?? val.ToString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            break;
                        }
                    }
                }

                var argProps = new[] { "arguments", "Arguments", "args", "Args", "extraArguments", "ExtraArguments" };
                foreach (var prop in argProps)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        arguments = val.GetString() ?? val.ToString();
                        if (!string.IsNullOrWhiteSpace(arguments))
                        {
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    return (path, string.IsNullOrWhiteSpace(arguments) ? null : arguments);
                }
            }
            catch
            {
                // Fall back to query string / raw string
            }
        }

        if (trimmed.Contains("path=", StringComparison.OrdinalIgnoreCase))
        {
            var matchPath = System.Text.RegularExpressions.Regex.Match(trimmed, @"path=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matchPath.Success)
            {
                var path = Uri.UnescapeDataString(matchPath.Groups[1].Value);
                string args = null;
                var matchArgs = System.Text.RegularExpressions.Regex.Match(trimmed, @"arguments=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (matchArgs.Success)
                {
                    args = Uri.UnescapeDataString(matchArgs.Groups[1].Value);
                }

                return (path, string.IsNullOrWhiteSpace(args) ? null : args);
            }
        }

        return (trimmed, null);
    }

    public async Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null)
    {
        var resolvedScriptPath = scriptPath;
        var resolvedArguments = arguments;

        if (!string.IsNullOrWhiteSpace(scriptPath) && scriptPath.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            var (parsedPath, parsedArgs) = ParseSettings(scriptPath);
            if (!string.IsNullOrWhiteSpace(parsedPath))
            {
                resolvedScriptPath = parsedPath;
                if (string.IsNullOrWhiteSpace(resolvedArguments))
                {
                    resolvedArguments = parsedArgs;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedScriptPath) || !File.Exists(resolvedScriptPath))
        {
            this.logger.Warn("Custom script path does not exist: {0}", resolvedScriptPath);
            return false;
        }

        try
        {
            var workingDir = !string.IsNullOrWhiteSpace(torrent?.SavePath) && Directory.Exists(torrent.SavePath)
                ? torrent.SavePath
                : (Path.GetDirectoryName(resolvedScriptPath) ?? Environment.CurrentDirectory);

            var (resolvedFileName, resolvedArgs) = ResolveInterpreter(resolvedScriptPath, resolvedArguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedFileName,
                Arguments = resolvedArgs,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Sanitize inherited environment variables
            SanitizeEnvironment(startInfo.EnvironmentVariables);

            // Inject Servarr / Leecharr standard environment variables
            var meta = torrent != null ? this.mediaEnrichmentService?.GetMetadata(torrent.Id) : null;
            var envVars = BuildEnvironmentVariables(eventType, torrent, meta);
            foreach (var kvp in envVars)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            this.logger.Info("Executing custom script '{0}' for event '{1}' in working directory '{2}'...", resolvedScriptPath, eventType, workingDir);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            using var timeoutCts = new CancellationTokenSource(this.scriptTimeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                this.logger.Error("Custom script timed out after {0}s: {1}", this.scriptTimeout.TotalSeconds, resolvedScriptPath);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // Ignore kill exception
                }

                return false;
            }

            var stdout = string.Empty;
            var stderr = string.Empty;

            try
            {
                using var drainCts = new CancellationTokenSource(this.streamDrainTimeout);
                using var linkedDrainCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, drainCts.Token);

                try
                {
                    await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(linkedDrainCts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
                {
                    this.logger.Debug("Custom script stream draining timed out after process exit: {0}", resolvedScriptPath);
                }

                if (stdoutTask.IsCompletedSuccessfully)
                {
                    stdout = stdoutTask.Result;
                }

                if (stderrTask.IsCompletedSuccessfully)
                {
                    stderr = stderrTask.Result;
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Exception while draining custom script streams: {0}", resolvedScriptPath);
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                this.logger.Debug("Custom script stdout: {0}", stdout.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                this.logger.Warn("Custom script stderr: {0}", stderr.Trim());
            }

            this.logger.Info("Custom script '{0}' completed with exit code: {1}", resolvedScriptPath, process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to execute custom script: {0}", resolvedScriptPath);
            return false;
        }
    }
}
