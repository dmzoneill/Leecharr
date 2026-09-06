using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Leecharr.Http;
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

public class FileBrowserBatchDeleteRequest
{
    public List<string> Paths { get; set; } = new();
}

public class FileBrowserTransferRequest
{
    public List<string> Sources { get; set; } = new();

    public string Destination { get; set; }

    public string Operation { get; set; } = "copy";
}

[V1ApiController("files")]
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

    [HttpPost("batch-delete")]
    public ActionResult BatchDelete([FromBody] FileBrowserBatchDeleteRequest request)
    {
        if (request == null || request.Paths == null || request.Paths.Count == 0)
        {
            return this.BadRequest(new { Message = "No paths provided for deletion." });
        }

        var deleted = 0;
        var failed = new List<string>();

        foreach (var path in request.Paths)
        {
            try
            {
                this.fileBrowserService.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                failed.Add($"{path}: {ex.Message}");
            }
        }

        return this.Ok(new
        {
            Success = failed.Count == 0,
            DeletedCount = deleted,
            Failed = failed,
        });
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.BadRequest(new { Message = "A path is required." });
        }

        var fullPath = this.fileBrowserService.ResolvePath(path);
        if (!global::System.IO.File.Exists(fullPath))
        {
            return this.NotFound(new { Message = "File not found." });
        }

        var fileName = Path.GetFileName(fullPath);
        return this.PhysicalFile(fullPath, "application/octet-stream", fileName, enableRangeProcessing: true);
    }

    [HttpGet("preview")]
    public ActionResult GetPreview([FromQuery] string path, [FromQuery] int maxBytes = 262144)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.BadRequest(new { Message = "A path is required." });
        }

        var fullPath = this.fileBrowserService.ResolvePath(path);
        if (!global::System.IO.File.Exists(fullPath))
        {
            return this.NotFound(new { Message = "File not found." });
        }

        var fileInfo = new FileInfo(fullPath);
        var ext = (fileInfo.Extension ?? string.Empty).TrimStart('.').ToLowerInvariant();

        var isText = ext is "txt" or "nfo" or "log" or "srt" or "vtt" or "sub" or "ass" or "json" or "xml" or "yml" or "yaml" or "md" or "ini" or "conf" or "cfg" or "sh" or "bat" or "py" or "csv" or "torrent";
        var isImage = ext is "jpg" or "jpeg" or "png" or "gif" or "webp" or "svg" or "bmp" or "ico";

        if (isText)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var readLength = (int)Math.Min(stream.Length, maxBytes);
            var buffer = new byte[readLength];
            var bytesRead = stream.Read(buffer, 0, readLength);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var truncated = stream.Length > maxBytes;

            return this.Ok(new
            {
                Type = "text",
                Name = fileInfo.Name,
                Path = fullPath,
                Size = fileInfo.Length,
                Content = content,
                Truncated = truncated,
                Extension = ext,
            });
        }

        if (isImage)
        {
            return this.Ok(new
            {
                Type = "image",
                Name = fileInfo.Name,
                Path = fullPath,
                Size = fileInfo.Length,
                Extension = ext,
                DownloadUrl = $"/api/v1/files/download?path={Uri.EscapeDataString(path)}",
            });
        }

        return this.Ok(new
        {
            Type = "binary",
            Name = fileInfo.Name,
            Path = fullPath,
            Size = fileInfo.Length,
            Extension = ext,
            DownloadUrl = $"/api/v1/files/download?path={Uri.EscapeDataString(path)}",
        });
    }

    [HttpPost("paste")]
    public ActionResult Paste([FromBody] FileBrowserTransferRequest request)
    {
        if (request == null || request.Sources == null || request.Sources.Count == 0 || string.IsNullOrWhiteSpace(request.Destination))
        {
            return this.BadRequest(new { Message = "Sources and Destination are required." });
        }

        var isMove = string.Equals(request.Operation, "move", StringComparison.OrdinalIgnoreCase);
        var successCount = 0;
        var failed = new List<string>();

        foreach (var src in request.Sources)
        {
            try
            {
                if (isMove)
                {
                    this.fileBrowserService.Move(src, request.Destination);
                }
                else
                {
                    this.fileBrowserService.Copy(src, request.Destination);
                }

                successCount++;
            }
            catch (Exception ex)
            {
                failed.Add($"{src}: {ex.Message}");
            }
        }

        return this.Ok(new
        {
            Success = failed.Count == 0,
            Count = successCount,
            Failed = failed,
        });
    }

    [HttpPost("upload")]
    [RequestSizeLimit(1073741824)] // 1 GB
    public async Task<ActionResult> Upload([FromQuery] string path = null)
    {
        var targetDir = this.fileBrowserService.ResolvePath(path);
        if (!global::System.IO.Directory.Exists(targetDir))
        {
            global::System.IO.Directory.CreateDirectory(targetDir);
        }

        var files = this.Request.Form.Files;
        if (files == null || files.Count == 0)
        {
            return this.BadRequest(new { Message = "No files uploaded." });
        }

        var uploaded = new List<string>();
        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                var targetFile = Path.Combine(targetDir, Path.GetFileName(file.FileName));
                using var stream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await file.CopyToAsync(stream);
                uploaded.Add(targetFile);
            }
        }

        return this.Ok(new { Success = true, Uploaded = uploaded });
    }
}
