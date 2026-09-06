using System;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
[Authorize(Policy = "RequireAdmin")]
public class FileBrowserController : Controller
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly IFileBrowserService fileBrowserService;

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
            this.logger.Warn(ex, "Access denied in file browser listing");
            return this.StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access to the specified path is denied." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error listing directory in file browser");
            return this.BadRequest(new { Message = "Failed to list directory." });
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
            this.logger.Warn(ex, "Access denied when creating directory in file browser");
            return this.StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access to the specified path is denied." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to create directory in file browser");
            return this.BadRequest(new { Message = "Failed to create directory." });
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
            this.logger.Warn(ex, "Access denied when renaming item in file browser");
            return this.StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access to the specified path is denied." });
        }
        catch (ArgumentException ex)
        {
            this.logger.Warn(ex, "Invalid rename argument in file browser");
            return this.BadRequest(new { Message = "Invalid path or file name." });
        }
        catch (InvalidOperationException ex)
        {
            this.logger.Warn(ex, "Invalid rename operation in file browser");
            return this.BadRequest(new { Message = "Destination already exists." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to rename item in file browser");
            return this.BadRequest(new { Message = "Failed to rename item." });
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
            this.logger.Warn(ex, "Access denied when deleting item in file browser");
            return this.StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access to the specified path is denied." });
        }
        catch (InvalidOperationException ex)
        {
            this.logger.Warn(ex, "Invalid delete operation in file browser");
            return this.BadRequest(new { Message = "Cannot delete the specified path." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to delete item in file browser");
            return this.BadRequest(new { Message = "Failed to delete item." });
        }
    }
}
