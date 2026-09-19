using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Api.V1.FileBrowser;

public class FileBrowserPathRequest
{
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string Path { get; set; }
}

public class FileBrowserRenameRequest
{
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string Path { get; set; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string NewName { get; set; }
}

public class FileBrowserBatchDeleteRequest
{
    [Required]
    public List<string> Paths { get; set; } = new();
}

public class FileBrowserTransferRequest
{
    [Required]
    public List<string> Sources { get; set; } = new();

    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string Destination { get; set; }

    [StringLength(50)]
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
            var target = this.fileBrowserService.ResolvePath(request.Path);
            if (this.fileBrowserService.IsRootPath(target) || this.fileBrowserService.IsRootOrSystemDirectory(target))
            {
                return this.BadRequest(new { Message = $"Cannot create directory in root or system path '{target}'." });
            }

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
            var target = this.fileBrowserService.ResolvePath(request.Path);
            if (this.fileBrowserService.IsRootPath(target) || this.fileBrowserService.IsRootOrSystemDirectory(target))
            {
                return this.BadRequest(new { Message = $"Cannot rename root or system path '{target}'." });
            }

            var parent = Path.GetDirectoryName(target);
            var dest = !string.IsNullOrEmpty(parent) ? Path.Combine(parent, request.NewName.Trim()) : null;
            if (dest != null && (this.fileBrowserService.IsRootPath(dest) || this.fileBrowserService.IsRootOrSystemDirectory(dest)))
            {
                return this.BadRequest(new { Message = $"Cannot rename to root or system path '{dest}'." });
            }

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
            var target = this.fileBrowserService.ResolvePath(path);
            if (this.fileBrowserService.IsRootPath(target) || this.fileBrowserService.IsRootOrSystemDirectory(target))
            {
                return this.BadRequest(new { Message = $"Cannot delete root or system path '{target}'." });
            }

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
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var target = this.fileBrowserService.ResolvePath(path);
                if (this.fileBrowserService.IsRootPath(target) || this.fileBrowserService.IsRootOrSystemDirectory(target))
                {
                    failed.Add($"{path}: Cannot delete root or system path '{target}'.");
                    continue;
                }

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

        string fullPath;
        try
        {
            fullPath = this.fileBrowserService.ResolvePath(path);
        }
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }

        if (this.fileBrowserService.IsRootPath(fullPath) || this.fileBrowserService.IsRootOrSystemDirectory(fullPath))
        {
            return this.BadRequest(new { Message = $"Access to root or system path '{fullPath}' is denied." });
        }

        if (!global::System.IO.File.Exists(fullPath))
        {
            return this.NotFound(new { Message = "File not found." });
        }

        var fileName = Path.GetFileName(fullPath);
        var ext = Path.GetExtension(fullPath)?.TrimStart('.').ToLowerInvariant();
        var contentType = ext switch
        {
            "mp4" => "video/mp4",
            "mkv" => "video/x-matroska",
            "webm" => "video/webm",
            "avi" => "video/x-msvideo",
            "mov" => "video/quicktime",
            "mp3" => "audio/mpeg",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "ogg" => "audio/ogg",
            "m4a" => "audio/mp4",
            "aac" => "audio/aac",
            "opus" => "audio/opus",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

        return this.PhysicalFile(fullPath, contentType, fileName, enableRangeProcessing: true);
    }

    [HttpGet("preview")]
    public ActionResult GetPreview([FromQuery] string path, [FromQuery] int maxBytes = 262144)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.BadRequest(new { Message = "A path is required." });
        }

        string fullPath;
        try
        {
            fullPath = this.fileBrowserService.ResolvePath(path);
        }
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }

        if (this.fileBrowserService.IsRootPath(fullPath) || this.fileBrowserService.IsRootOrSystemDirectory(fullPath))
        {
            return this.BadRequest(new { Message = $"Access to root or system path '{fullPath}' is denied." });
        }

        var clampedMaxBytes = Math.Clamp(maxBytes <= 0 ? 262144 : maxBytes, 1, 10 * 1024 * 1024);

        if (!global::System.IO.File.Exists(fullPath))
        {
            return this.NotFound(new { Message = "File not found." });
        }

        var fileInfo = new FileInfo(fullPath);
        var ext = (fileInfo.Extension ?? string.Empty).TrimStart('.').ToLowerInvariant();

        var isText = ext is "txt" or "nfo" or "log" or "srt" or "vtt" or "sub" or "ass" or "json" or "xml" or "yml" or "yaml" or "md" or "ini" or "conf" or "cfg" or "sh" or "bat" or "py" or "csv" or "torrent";
        var isImage = ext is "jpg" or "jpeg" or "png" or "gif" or "webp" or "svg" or "bmp" or "ico";
        var isVideo = ext is "mp4" or "mkv" or "webm" or "avi" or "mov" or "m4v" or "ogv" or "ts";
        var isAudio = ext is "mp3" or "flac" or "wav" or "ogg" or "m4a" or "aac" or "opus" or "wma";

        if (isText)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var readLength = (int)Math.Min(stream.Length, clampedMaxBytes);
            var buffer = new byte[readLength];
            var bytesRead = stream.Read(buffer, 0, readLength);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var truncated = stream.Length > clampedMaxBytes;

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

        if (isVideo)
        {
            return this.Ok(new
            {
                Type = "video",
                Name = fileInfo.Name,
                Path = fullPath,
                Size = fileInfo.Length,
                Extension = ext,
                DownloadUrl = $"/api/v1/files/download?path={Uri.EscapeDataString(path)}",
            });
        }

        if (isAudio)
        {
            return this.Ok(new
            {
                Type = "audio",
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

        string destDir;
        try
        {
            destDir = this.fileBrowserService.ResolvePath(request.Destination);
        }
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }

        if (this.fileBrowserService.IsRootPath(destDir) || this.fileBrowserService.IsRootOrSystemDirectory(destDir))
        {
            return this.BadRequest(new { Message = $"Destination cannot be root or system path '{destDir}'." });
        }

        var isMove = string.Equals(request.Operation, "move", StringComparison.OrdinalIgnoreCase);
        var successCount = 0;
        var failed = new List<string>();

        foreach (var src in request.Sources)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(src))
                {
                    continue;
                }

                var srcResolved = this.fileBrowserService.ResolvePath(src);
                if (this.fileBrowserService.IsRootPath(srcResolved) || this.fileBrowserService.IsRootOrSystemDirectory(srcResolved))
                {
                    failed.Add($"{src}: Cannot transfer root or system path '{srcResolved}'.");
                    continue;
                }

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
        string targetDir;
        try
        {
            targetDir = this.fileBrowserService.ResolvePath(path);
        }
        catch (Exception ex)
        {
            return this.BadRequest(new { Message = ex.Message });
        }

        if (this.fileBrowserService.IsRootPath(targetDir) || this.fileBrowserService.IsRootOrSystemDirectory(targetDir))
        {
            return this.BadRequest(new { Message = $"Cannot upload to root or system path '{targetDir}'." });
        }

        if (!global::System.IO.Directory.Exists(targetDir))
        {
            global::System.IO.Directory.CreateDirectory(targetDir);
        }

        if (this.Request?.HasFormContentType != true || this.Request.Form?.Files == null || this.Request.Form.Files.Count == 0)
        {
            return this.BadRequest(new { Message = "No files uploaded." });
        }

        var files = this.Request.Form.Files;
        var uploaded = new List<string>();
        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                var cleanFileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(cleanFileName) || cleanFileName.Contains('\0'))
                {
                    continue;
                }

                var targetFile = Path.Combine(targetDir, cleanFileName);
                if (this.fileBrowserService.IsRootOrSystemDirectory(targetFile))
                {
                    continue;
                }

                using var stream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await file.CopyToAsync(stream);
                uploaded.Add(targetFile);
            }
        }

        return this.Ok(new { Success = true, Uploaded = uploaded });
    }
}
