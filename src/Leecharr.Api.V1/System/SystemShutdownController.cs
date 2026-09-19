// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/shutdown")]
public class SystemShutdownController : ControllerBase
{
    private readonly IRuntimeInfo runtimeInfo;
    private readonly IHostApplicationLifetime hostApplicationLifetime;

    public SystemShutdownController(IRuntimeInfo runtimeInfo = null, IHostApplicationLifetime hostApplicationLifetime = null)
    {
        this.runtimeInfo = runtimeInfo;
        this.hostApplicationLifetime = hostApplicationLifetime;
    }

    [HttpPost]
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
}
