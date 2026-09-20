// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace Leecharr.Api.V1.System;

public class SystemStatusResource
{
    public string AppName => "Leecharr";

    public string Version => BuildInfo.Version.ToString();

    public string Branch => BuildInfo.Branch;

    public string InstanceUuid { get; set; }

    public string OsName { get; set; }

    public string OsVersion { get; set; }

    public string RuntimeName => ".NET";

    public string RuntimeVersion { get; set; }

    public bool IsDocker { get; set; }

    public bool IsLinux { get; set; }

    public bool IsWindows { get; set; }

    public bool IsOsx { get; set; }

    public bool IsDebug
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public bool IsProduction => !this.IsDebug;

    public string AppDataFolder { get; set; }

    public string AppDataPath
    {
        get => this.AppDataFolder;
        set => this.AppDataFolder = value;
    }

    public string StartupPath { get; set; }

    public DateTime StartTime { get; set; } = SystemController.AppStartTime;

    public int UptimeSeconds => (int)Math.Max(0, (DateTime.UtcNow - this.StartTime).TotalSeconds);

    public string DatabaseType { get; set; } = "SQLite";

    public string DatabaseVersion { get; set; } = "SQLite";

    public string DatabaseMigration { get; set; } = "18";
}

[V1ApiController("system/status")]
public class SystemController : ControllerBase
{
    internal static readonly DateTime AppStartTime = DateTime.UtcNow;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IDatabase database;
    private readonly IRuntimeInfo runtimeInfo;
    private readonly IHostApplicationLifetime hostApplicationLifetime;
    private readonly IConfigService configService;

    public SystemController(
        IAppFolderInfo appFolderInfo,
        IDatabase database = null,
        IRuntimeInfo runtimeInfo = null,
        IHostApplicationLifetime hostApplicationLifetime = null,
        IConfigService configService = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.database = database;
        this.runtimeInfo = runtimeInfo;
        this.hostApplicationLifetime = hostApplicationLifetime;
        this.configService = configService;
    }

    [NonAction]
    public ActionResult Restart()
    {
        if (this.runtimeInfo != null)
        {
            this.runtimeInfo.RestartPending = true;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            this.hostApplicationLifetime?.StopApplication();
        });

        return this.Ok(new { message = "Restarting Leecharr..." });
    }

    [NonAction]
    public ActionResult Shutdown()
    {
        if (this.runtimeInfo != null)
        {
            this.runtimeInfo.RestartPending = false;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            this.hostApplicationLifetime?.StopApplication();
        });

        return this.Ok(new { message = "Shutting down Leecharr..." });
    }

    [HttpGet]
    public ActionResult<SystemStatusResource> GetStatus()
    {
        var migration = "18";
        var dbType = this.database?.DatabaseType.ToString() ?? "SQLite";

        if (this.database != null)
        {
            try
            {
                using var conn = this.database.OpenConnection();
                var currentMigration = conn.ExecuteScalar<long?>("SELECT MAX(Version) FROM VersionInfo;");
                if (currentMigration.HasValue)
                {
                    migration = currentMigration.Value.ToString();
                }
            }
            catch
            {
                // Fallback to latest migration if VersionInfo table is not queryable
            }
        }

        return this.Ok(new SystemStatusResource
        {
            InstanceUuid = this.configService?.InstanceUuid ?? string.Empty,
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            IsDocker = OsInfo.IsDocker,
            IsLinux = OsInfo.IsLinux,
            IsWindows = OsInfo.IsWindows,
            IsOsx = OsInfo.IsOsx,
            AppDataFolder = SanitizeHostPath(this.appFolderInfo?.AppDataFolder),
            StartupPath = SanitizeHostPath(this.appFolderInfo?.StartUpFolder),
            StartTime = AppStartTime,
            DatabaseType = dbType,
            DatabaseVersion = dbType,
            DatabaseMigration = migration,
        });
    }

    public static string SanitizeHostPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = path.Substring(userProfile.Length);
                return remainder.StartsWith('/') || remainder.StartsWith('\\') ? "~" + remainder : "~/" + remainder;
            }
        }
        catch
        {
            // Ignore environment query exceptions
        }

        var homeRegex = new Regex(@"^(/home/[^/\\]+|/Users/[^/\\]+|[a-zA-Z]:\\Users\\[^/\\]+)", RegexOptions.IgnoreCase);
        var match = homeRegex.Match(path);
        if (match.Success)
        {
            var remainder = path.Substring(match.Length);
            return remainder.StartsWith('/') || remainder.StartsWith('\\') ? "~" + remainder : "~/" + remainder;
        }

        return path;
    }
}
