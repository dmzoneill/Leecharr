using System;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IFileBrowserService fileBrowserService;

    public FileBrowserController(IFileBrowserService fileBrowserService)
    {
        this.fileBrowserService = fileBrowserService;
    }

    [HttpGet]
    public ActionResult<FileBrowserListing> GetListing([FromQuery] string path = null)
    {
        return this.Ok(this.fileBrowserService.ListDirectory(path));
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
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
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
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
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
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }
    }
}
