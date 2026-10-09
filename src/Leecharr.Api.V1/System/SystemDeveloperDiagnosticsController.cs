// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Developer.Diagnostics;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/developer/diagnostics")]
public class SystemDeveloperDiagnosticsController : Controller
{
    private readonly IDeveloperDiagnosticsService diagnosticsService;

    public SystemDeveloperDiagnosticsController(IDeveloperDiagnosticsService diagnosticsService = null)
    {
        this.diagnosticsService = diagnosticsService;
    }

    [HttpGet("threads")]
    public ActionResult<IReadOnlyList<ThreadDiagnosticItem>> GetThreads()
    {
        if (this.diagnosticsService == null)
        {
            return this.Ok(Array.Empty<ThreadDiagnosticItem>());
        }

        return this.Ok(this.diagnosticsService.GetThreads());
    }

    [HttpGet("memory")]
    public ActionResult<MemoryDiagnosticReport> GetMemory()
    {
        if (this.diagnosticsService == null)
        {
            return new MemoryDiagnosticReport();
        }

        return this.Ok(this.diagnosticsService.GetMemoryReport());
    }

    [HttpGet("environment")]
    public ActionResult<EnvironmentDiagnosticReport> GetEnvironment()
    {
        if (this.diagnosticsService == null)
        {
            return new EnvironmentDiagnosticReport();
        }

        return this.Ok(this.diagnosticsService.GetEnvironmentReport());
    }

    [HttpPost("memory/gc")]
    public ActionResult<GcCollectionResponse> CollectGarbage([FromBody] GcCollectionRequest request)
    {
        if (this.diagnosticsService == null)
        {
            return new GcCollectionResponse { Success = false, Message = "Diagnostics service not registered." };
        }

        var response = this.diagnosticsService.ForceGarbageCollection(request ?? new GcCollectionRequest());
        return this.Ok(response);
    }
}
