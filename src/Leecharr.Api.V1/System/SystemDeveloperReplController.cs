// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Developer.Repl;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/developer/repl")]
public class SystemDeveloperReplController : Controller
{
    private readonly IDeveloperReplService replService;

    public SystemDeveloperReplController(IDeveloperReplService replService = null)
    {
        this.replService = replService;
    }

    [HttpPost("eval")]
    public ActionResult<ReplExecutionResponse> Evaluate([FromBody] ReplExecutionRequest request)
    {
        if (this.replService == null)
        {
            return new ReplExecutionResponse
            {
                Success = false,
                ErrorMessage = "Developer REPL service is not registered.",
            };
        }

        var response = this.replService.Execute(request ?? new ReplExecutionRequest());
        return this.Ok(response);
    }

    [HttpGet("history")]
    public ActionResult<IReadOnlyList<ReplHistoryEntry>> GetHistory([FromQuery] int limit = 50)
    {
        if (this.replService == null)
        {
            return new List<ReplHistoryEntry>();
        }

        return this.Ok(this.replService.GetHistory(limit));
    }

    [HttpDelete("history")]
    public ActionResult ClearHistory()
    {
        this.replService?.ClearHistory();
        return this.NoContent();
    }

    [HttpDelete("session")]
    public ActionResult ResetSession()
    {
        this.replService?.ResetSession();
        return this.NoContent();
    }
}
