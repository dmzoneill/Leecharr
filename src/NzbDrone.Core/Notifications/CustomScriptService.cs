using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public interface ICustomScriptService
{
    Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType);
}

public class CustomScriptService : ICustomScriptService
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public async Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
        {
            _logger.Warn("Custom script path does not exist: {0}", scriptPath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Inject Servarr / Leecharr standard environment variables
            startInfo.EnvironmentVariables["LEECHARR_EVENT_TYPE"] = eventType;
            if (torrent != null)
            {
                startInfo.EnvironmentVariables["TORRENT_ID"] = torrent.Id.ToString();
                startInfo.EnvironmentVariables["TORRENT_NAME"] = torrent.Name ?? string.Empty;
                startInfo.EnvironmentVariables["TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
                startInfo.EnvironmentVariables["TORRENT_CATEGORY"] = torrent.Category ?? string.Empty;
                startInfo.EnvironmentVariables["TORRENT_PATH"] = torrent.SavePath ?? string.Empty;
                startInfo.EnvironmentVariables["TORRENT_SIZE"] = torrent.TotalSize.ToString();
            }

            _logger.Info("Executing custom script '{0}' for event '{1}'...", scriptPath, eventType);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var processTask = process.WaitForExitAsync();

            if (await Task.WhenAny(processTask, timeoutTask) == timeoutTask)
            {
                _logger.Error("Custom script timed out after 60s: {0}", scriptPath);
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

            _logger.Info("Custom script '{0}' completed with exit code: {1}", scriptPath, process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute custom script: {0}", scriptPath);
            return false;
        }
    }
}
