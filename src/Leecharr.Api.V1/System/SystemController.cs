// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Runtime.InteropServices;
using Dapper;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;

namespace Leecharr.Api.V1.System;

public class SystemStatusResource
{
    public string AppName => "Leecharr";

    public string Version => BuildInfo.Version.ToString();

    public string Branch => BuildInfo.Branch;

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

    public SystemController(IAppFolderInfo appFolderInfo, IDatabase database = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.database = database;
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
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            IsDocker = OsInfo.IsDocker,
            IsLinux = OsInfo.IsLinux,
            IsWindows = OsInfo.IsWindows,
            IsOsx = OsInfo.IsOsx,
            AppDataFolder = this.appFolderInfo?.AppDataFolder,
            StartupPath = this.appFolderInfo?.StartUpFolder,
            StartTime = AppStartTime,
            DatabaseType = dbType,
            DatabaseVersion = dbType,
            DatabaseMigration = migration,
        });
    }
}
