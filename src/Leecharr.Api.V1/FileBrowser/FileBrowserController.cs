// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Api.V1.FileBrowser;

public class FileBrowserPathRequest
{
    public string Path { get; set; }
}

public class FileBrowserRenameRequest
{
    public string Path { get; set; }

    public string NewName { get; set; }
}

[V1ApiController("files")]
public class FileBrowserController : Controller
{
    private readonly IFileBrowserService fileBrowserService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public FileBrowserController(IFileBrowserService fileBrowserService)
    {
        this.fileBrowserService = fileBrowserService;
    }

    [HttpGet]
    public ActionResult<FileBrowserListing> GetListing([FromQuery] string path = null)
    {
        try
        {
            return this.Ok(this.fileBrowserService.ListDirectory(path));
        }
        catch (UnauthorizedAccessException ex)
        {
            this.logger.Warn("Unauthorized file browser access: {0}", ex.Message);
            return this.BadRequest(new { Message = "Access to the requested path is denied." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error listing directory");
            return this.BadRequest(new { Message = "Unable to list directory." });
        }
    }

    [HttpPost("mkdir")]
    public ActionResult CreateDirectory([FromBody] FileBrowserPathRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Path))
        {
            return this.BadRequest(new { Message = "A path is required." });
        }

        try
        {
            this.fileBrowserService.CreateDirectory(request.Path);
            return this.Ok(new { Success = true, Path = request.Path });
        }
        catch (UnauthorizedAccessException ex)
        {
            this.logger.Warn("Unauthorized directory creation: {0}", ex.Message);
            return this.BadRequest(new { Message = "Access to the requested path is denied." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error creating directory");
            return this.BadRequest(new { Message = "Unable to create directory." });
        }
    }

    [HttpPut("rename")]
    public ActionResult Rename([FromBody] FileBrowserRenameRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.NewName))
        {
            return this.BadRequest(new { Message = "Both path and newName are required." });
        }

        try
        {
            this.fileBrowserService.Rename(request.Path, request.NewName.Trim());
            return this.Ok(new { Success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            this.logger.Warn("Unauthorized file rename: {0}", ex.Message);
            return this.BadRequest(new { Message = "Access to the requested path is denied." });
        }
        catch (ArgumentException ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error renaming path");
            return this.BadRequest(new { Message = "Unable to rename item." });
        }
    }

    [HttpDelete]
    public ActionResult Delete([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.BadRequest(new { Message = "A path is required." });
        }

        try
        {
            this.fileBrowserService.Delete(path);
            return this.Ok(new { Success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            this.logger.Warn("Unauthorized file delete: {0}", ex.Message);
            return this.BadRequest(new { Message = "Access to the requested path is denied." });
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error deleting path");
            return this.BadRequest(new { Message = "Unable to delete item." });
        }
    }
}
