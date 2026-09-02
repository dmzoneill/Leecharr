// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.System;

[V1ApiController("log")]
public class LogController : ControllerBase
{
    private readonly IConfigService configService;

    public LogController(IConfigService configService)
    {
        this.configService = configService;
    }

    [HttpGet]
    public ActionResult<List<LogResource>> GetLogs(
        [FromQuery] string level = null,
        [FromQuery] int count = 500)
    {
        if (count < 1)
        {
            count = 1;
        }

        if (count > 5000)
        {
            count = 5000;
        }

        var minimumLevel = LogLevel.Trace;

        if (!string.IsNullOrWhiteSpace(level) && !string.Equals(level, "all", StringComparison.OrdinalIgnoreCase))
        {
            minimumLevel = ParseLogLevel(level) ?? minimumLevel;
        }

        var target = RingBufferTarget.Instance;

        if (target == null)
        {
            return this.Ok(new List<LogResource>());
        }

        var entries = target.GetEntries(count, minimumLevel);

        var resources = entries.Select((e, i) => new LogResource
        {
            Id = i + 1,
            Time = e.Time.ToString("O"),
            Level = e.Level,
            Logger = e.Logger,
            Message = e.Message,
            Exception = e.Exception,
        }).ToList();

        return this.Ok(resources);
    }

    private static LogLevel ParseLogLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return null;
        }

        try
        {
            return LogLevel.FromString(level.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public class LogResource
{
    public int Id { get; set; }

    public string Time { get; set; }

    public string Level { get; set; }

    public string Logger { get; set; }

    public string Message { get; set; }

    public string Exception { get; set; }
}
