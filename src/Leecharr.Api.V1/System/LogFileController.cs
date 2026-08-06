using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using Leecharr.Http;

namespace Leecharr.Api.V1.System;

[V1ApiController("logfile")]
public class LogFileController : ControllerBase
{
    private readonly IAppFolderInfo _appFolderInfo;

    public LogFileController(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
    }

    [HttpGet]
    public ActionResult<List<LogFileResource>> GetLogFiles()
    {
        var logDir = Path.Combine(_appFolderInfo.AppDataFolder, "logs");

        if (!Directory.Exists(logDir))
        {
            return Ok(new List<LogFileResource>());
        }

        var files = Directory.GetFiles(logDir, "*.*", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LogFileResource
            {
                Filename = f.Name,
                LastWriteTime = f.LastWriteTimeUtc,
                Size = f.Length
            })
            .ToList();

        return Ok(files);
    }

    [HttpGet("{filename}")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Filename is sanitized via Path.GetFileName and validated against the log directory")]
    public ActionResult GetLogFile(string filename)
    {
        var sanitized = Path.GetFileName(filename);

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized != filename)
        {
            return BadRequest("Invalid filename");
        }

        var logDir = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "logs"));
        var fullPath = Path.GetFullPath(Path.Combine(logDir, sanitized));

        if (!fullPath.StartsWith(logDir, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Access denied");
        }

        if (!global::System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var stream = global::System.IO.File.OpenRead(fullPath);
        return File(stream, "text/plain", sanitized);
    }
}
