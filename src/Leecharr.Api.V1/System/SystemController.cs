// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Runtime.InteropServices;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Api.V1.System;

public class SystemStatusResource
{
    public string AppName => "Leecharr";

    public string Version => BuildInfo.Version.ToString();

    public string OsName { get; set; }

    public string OsVersion { get; set; }

    public string RuntimeVersion { get; set; }

    public bool IsDocker { get; set; }

    public bool IsLinux { get; set; }

    public bool IsWindows { get; set; }

    public bool IsOsx { get; set; }

    public string AppDataFolder { get; set; }

    public DateTime StartTime { get; set; }
}

[V1ApiController("system/status")]
public class SystemController : ControllerBase
{
    private static readonly DateTime AppStartTime = DateTime.UtcNow;
    private readonly IAppFolderInfo appFolderInfo;

    public SystemController(IAppFolderInfo appFolderInfo)
    {
        this.appFolderInfo = appFolderInfo;
    }

    [HttpGet]
    public ActionResult<SystemStatusResource> GetStatus()
    {
        return this.Ok(new SystemStatusResource
        {
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            IsDocker = OsInfo.IsDocker,
            IsLinux = OsInfo.IsLinux,
            IsWindows = OsInfo.IsWindows,
            IsOsx = OsInfo.IsOsx,
            AppDataFolder = this.appFolderInfo.AppDataFolder,
            StartTime = AppStartTime,
        });
    }
}
