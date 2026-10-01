// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Developer.Debugger;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/developer/debugger")]
public class SystemDeveloperDebuggerController : Controller
{
    private readonly IDeveloperDebuggerService debuggerService;

    public SystemDeveloperDebuggerController(IDeveloperDebuggerService debuggerService = null)
    {
        this.debuggerService = debuggerService;
    }

    [HttpGet("status")]
    public ActionResult<DebuggerStatusReport> GetStatus()
    {
        if (this.debuggerService == null)
        {
            return new DebuggerStatusReport();
        }

        return this.Ok(this.debuggerService.GetStatus());
    }

    [HttpGet("tracepoints")]
    public ActionResult<IReadOnlyList<TracepointDefinition>> GetTracepoints()
    {
        if (this.debuggerService == null)
        {
            return new List<TracepointDefinition>();
        }

        return this.Ok(this.debuggerService.GetTracepoints());
    }

    [HttpPost("tracepoints")]
    public ActionResult<TracepointDefinition> AddTracepoint([FromBody] TracepointDefinition tracepoint)
    {
        if (this.debuggerService == null)
        {
            return this.BadRequest("Debugger service not registered.");
        }

        var created = this.debuggerService.AddTracepoint(tracepoint ?? new TracepointDefinition());
        return this.Ok(created);
    }

    [HttpDelete("tracepoints/{id}")]
    public ActionResult RemoveTracepoint(string id)
    {
        if (this.debuggerService == null)
        {
            return this.NotFound();
        }

        var removed = this.debuggerService.RemoveTracepoint(id);
        return removed ? this.NoContent() : this.NotFound();
    }

    [HttpDelete("tracepoints")]
    public ActionResult ClearTracepoints()
    {
        this.debuggerService?.ClearTracepoints();
        return this.NoContent();
    }

    [HttpGet("snapshots")]
    public ActionResult<IReadOnlyList<TracepointSnapshot>> GetSnapshots([FromQuery] int limit = 50)
    {
        if (this.debuggerService == null)
        {
            return new List<TracepointSnapshot>();
        }

        return this.Ok(this.debuggerService.GetSnapshots(limit));
    }

    [HttpPost("snapshots")]
    public ActionResult<TracepointSnapshot> RecordSnapshot([FromBody] TracepointSnapshot snapshot)
    {
        if (this.debuggerService == null)
        {
            return this.BadRequest("Debugger service not registered.");
        }

        var toRecord = snapshot ?? new TracepointSnapshot();
        this.debuggerService.RecordSnapshot(toRecord);
        return this.Ok(toRecord);
    }

    [HttpDelete("snapshots")]
    public ActionResult ClearSnapshots()
    {
        this.debuggerService?.ClearSnapshots();
        return this.NoContent();
    }

    [HttpGet("files")]
    public ActionResult<IReadOnlyList<DebuggerSourceFileItem>> GetFiles()
    {
        if (this.debuggerService == null)
        {
            return new List<DebuggerSourceFileItem>();
        }

        return this.Ok(this.debuggerService.GetKnownSourceFiles());
    }

    [HttpGet("source")]
    public ActionResult<DebuggerSourceCodeResponse> GetSource([FromQuery] string path)
    {
        if (this.debuggerService == null)
        {
            return new DebuggerSourceCodeResponse { FilePath = path, Exists = false, Content = "// Debugger service not registered" };
        }

        return this.Ok(this.debuggerService.GetSourceCode(path));
    }
}
