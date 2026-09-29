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
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
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

        var resources = entries.Select(e => new LogResource
        {
            Id = e.Id,
            Time = e.Time.ToString("O"),
            Level = e.Level,
            Logger = e.Logger,
            Message = e.Message,
            Exception = e.Exception,
        }).ToList();

        return this.Ok(resources);
    }

    [HttpPost("test")]
    [HttpPost("emit")]
    public ActionResult<TestLogResponse> TestLog([FromBody] TestLogRequest request = null)
    {
        var level = ParseLogLevel(request?.Level) ?? LogLevel.Info;
        var message = string.IsNullOrWhiteSpace(request?.Message)
            ? $"Diagnostic test log message generated from UI ({level})"
            : request.Message.Trim();

        Logger.Log(level, message);

        return this.Ok(new TestLogResponse
        {
            Success = true,
            Level = level.ToString(),
            Message = message,
        });
    }

    private static LogLevel ParseLogLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return null;
        }

        var trimmed = level.Trim();
        if (string.Equals(trimmed, "warning", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Warn;
        }

        try
        {
            return LogLevel.FromString(trimmed);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public class TestLogRequest
{
    public string Level { get; set; }

    public string Message { get; set; }
}

public class TestLogResponse
{
    public bool Success { get; set; }

    public string Level { get; set; }

    public string Message { get; set; }
}

public class LogResource
{
    public long Id { get; set; }

    public string Time { get; set; }

    public string Level { get; set; }

    public string Logger { get; set; }

    public string Message { get; set; }

    public string Exception { get; set; }
}
