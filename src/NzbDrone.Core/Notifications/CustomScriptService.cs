// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
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
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CustomScriptService(IMediaEnrichmentService mediaEnrichmentService = null)
    {
        this.mediaEnrichmentService = mediaEnrichmentService;
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

    public async Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
        {
            this.logger.Warn("Custom script path does not exist: {0}", scriptPath);
            return false;
        }

        try
        {
            var workingDir = !string.IsNullOrWhiteSpace(torrent?.SavePath) && Directory.Exists(torrent.SavePath)
                ? torrent.SavePath
                : (Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = scriptPath,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Inject Servarr / Leecharr standard environment variables
            var meta = torrent != null ? this.mediaEnrichmentService?.GetMetadata(torrent.Id) : null;
            var envVars = BuildEnvironmentVariables(eventType, torrent, meta);
            foreach (var kvp in envVars)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            this.logger.Info("Executing custom script '{0}' for event '{1}' in working directory '{2}'...", scriptPath, eventType, workingDir);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60), timeoutCts.Token);
            var processTask = process.WaitForExitAsync();

            if (await Task.WhenAny(processTask, timeoutTask) == timeoutTask)
            {
                this.logger.Error("Custom script timed out after 60s: {0}", scriptPath);
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // Ignore kill exception
                }

                return false;
            }

            timeoutCts.Cancel();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                this.logger.Debug("Custom script stdout: {0}", stdout.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                this.logger.Warn("Custom script stderr: {0}", stderr.Trim());
            }

            this.logger.Info("Custom script '{0}' completed with exit code: {1}", scriptPath, process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to execute custom script: {0}", scriptPath);
            return false;
        }
    }
}
