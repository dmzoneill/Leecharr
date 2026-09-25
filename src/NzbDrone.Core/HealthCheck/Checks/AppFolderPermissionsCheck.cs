// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.HealthCheck.Checks;

public class AppFolderPermissionsCheck : IHealthCheck
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Func<string, bool> isWritableCheck;

    public AppFolderPermissionsCheck(
        IAppFolderInfo appFolderInfo,
        Func<string, bool> isWritableCheck = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.isWritableCheck = isWritableCheck;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (this.appFolderInfo == null || string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
        {
            return Task.FromResult(HealthCheckResult.Error("AppFolderPermissions", "AppData folder is not configured or path is empty."));
        }

        var appData = this.appFolderInfo.AppDataFolder;
        if (!this.CheckFolderWritable(appData, out var appDataError))
        {
            return Task.FromResult(HealthCheckResult.Error("AppFolderPermissions", $"AppData folder '{appData}' is not writable: {appDataError}"));
        }

        var unwritableSubfolders = new List<string>();

        var logsFolder = Path.Combine(appData, "logs");
        if (!this.CheckFolderWritable(logsFolder, out var logsError))
        {
            unwritableSubfolders.Add($"logs ('{logsFolder}': {logsError})");
        }

        var backupsFolder = Directory.Exists(Path.Combine(appData, "backups"))
            ? Path.Combine(appData, "backups")
            : Path.Combine(appData, "Backups");

        if (!this.CheckFolderWritable(backupsFolder, out var backupsError))
        {
            unwritableSubfolders.Add($"backups ('{backupsFolder}': {backupsError})");
        }

        if (unwritableSubfolders.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Warning("AppFolderPermissions", $"Application subfolders are not writable: {string.Join(", ", unwritableSubfolders)}"));
        }

        return Task.FromResult(HealthCheckResult.Ok("AppFolderPermissions"));
    }

    private bool CheckFolderWritable(string folderPath, out string error)
    {
        error = null;

        if (this.isWritableCheck != null)
        {
            try
            {
                var writable = this.isWritableCheck(folderPath);
                if (!writable)
                {
                    error = "Permission denied";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        try
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var testFile = Path.Combine(folderPath, $".perm_test_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(testFile, "write_test");
                File.Delete(testFile);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(testFile))
                    {
                        File.Delete(testFile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Trace(ex, "Failed to clean up permission test file '{0}'", testFile);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
