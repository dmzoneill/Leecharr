// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using NLog;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Packages;

public class PackageExportRequest
{
    public List<int> TorrentIds { get; set; } = new();

    public bool IncludePayload { get; set; }
}

[V1ApiController("packages")]
public class PackageController : Controller
{
    private static readonly HashSet<char> InvalidFileNameChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '\"', '<', '>', '|', ':', '*', '?', '\\', '/', ';' }));

    private readonly IPackageExportService packageExportService;
    private readonly ITorrentService torrentService;
    private readonly IPackageImportService packageImportService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public PackageController(
        IPackageExportService packageExportService,
        ITorrentService torrentService,
        IPackageImportService packageImportService = null)
    {
        this.packageExportService = packageExportService;
        this.torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        this.packageImportService = packageImportService;
    }

    [HttpGet("export")]
    [HttpGet("/api/v1/package/export")]
    public async Task<IActionResult> Export(
        [FromQuery] string torrentIds = null,
        [FromQuery] bool includePayload = false,
        CancellationToken cancellationToken = default)
    {
        var idList = this.ParseTorrentIds(torrentIds, this.Request?.Query?["torrentIds"] ?? StringValues.Empty);
        return await this.ExecuteExportAsync(idList, includePayload, cancellationToken);
    }

    [HttpPost("export")]
    [HttpPost("/api/v1/package/export")]
    public async Task<IActionResult> ExportPost(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PackageExportRequest request = null,
        [FromQuery] string torrentIds = null,
        [FromQuery] bool? includePayload = null,
        CancellationToken cancellationToken = default)
    {
        var idList = new List<int>();
        if (request?.TorrentIds != null && request.TorrentIds.Count > 0)
        {
            idList.AddRange(request.TorrentIds.Where(id => id > 0));
        }

        if (idList.Count == 0)
        {
            idList = this.ParseTorrentIds(torrentIds, this.Request?.Query?["torrentIds"] ?? StringValues.Empty);
        }

        var payload = request?.IncludePayload ?? includePayload ?? false;
        return await this.ExecuteExportAsync(idList, payload, cancellationToken);
    }

    private async Task<IActionResult> ExecuteExportAsync(List<int> idList, bool includePayload, CancellationToken cancellationToken)
    {
        if (idList.Count == 0)
        {
            return this.BadRequest(new { message = "At least one valid torrent ID must be provided." });
        }

        if (this.packageExportService == null)
        {
            return this.StatusCode(StatusCodes.Status501NotImplemented, new { message = "Package export service is not available." });
        }

        var existingTorrents = new List<Torrent>();
        foreach (var id in idList)
        {
            try
            {
                var torrent = this.torrentService.Get(id);
                if (torrent != null)
                {
                    existingTorrents.Add(torrent);
                }
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to retrieve torrent with ID {0} for package export", id);
            }
        }

        if (existingTorrents.Count == 0)
        {
            return this.NotFound(new { message = "None of the specified torrents were found." });
        }

        var filename = DeterminePackageFileName(existingTorrents);
        var memoryStream = new MemoryStream();

        try
        {
            await this.packageExportService.ExportPackageAsync(
                memoryStream,
                existingTorrents.Select(t => t.Id),
                includePayload,
                cancellationToken);

            memoryStream.Position = 0;
            return this.File(memoryStream, "application/x-leecharr-package", filename);
        }
        catch (Exception)
        {
            await memoryStream.DisposeAsync();
            throw;
        }
    }

    [HttpPost("import")]
    [HttpPost("/api/v1/package/import")]
    public async Task<IActionResult> Import(
        IFormFile file = null,
        string targetRootDir = null,
        string destinationPath = null,
        string destinationRoot = null,
        string sourcePrefix = null,
        string destinationPrefix = null,
        bool restoreTorrents = true,
        bool skipDuplicates = true,
        CancellationToken cancellationToken = default)
    {
        if (this.packageImportService == null)
        {
            return this.StatusCode(StatusCodes.Status501NotImplemented, new { message = "Package import service is not available." });
        }

        var archiveFile = file ?? this.Request?.Form?.Files?.FirstOrDefault();
        if (archiveFile == null || archiveFile.Length == 0)
        {
            return this.BadRequest(new { message = "No package archive file provided." });
        }

        try
        {
            var options = new PackageImportOptions
            {
                TargetRootDir = targetRootDir,
                DestinationPath = destinationPath,
                DestinationRoot = destinationRoot,
                SourcePrefix = sourcePrefix,
                DestinationPrefix = destinationPrefix,
                RestoreTorrents = restoreTorrents,
                SkipDuplicates = skipDuplicates,
            };

            await using var archiveStream = archiveFile.OpenReadStream();
            var result = await this.packageImportService.ImportPackageAsync(archiveStream, options, cancellationToken);

            return this.Ok(result);
        }
        catch (SecurityException ex)
        {
            return this.BadRequest(new { message = "Security violation: " + ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return this.StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to import package: " + ex.Message });
        }
    }

    public static string DeterminePackageFileName(IReadOnlyList<Torrent> torrents)
    {
        if (torrents == null || torrents.Count == 0)
        {
            return "package.leecharr";
        }

        if (torrents.Count == 1)
        {
            var single = torrents[0];
            var name = !string.IsNullOrWhiteSpace(single?.Name) ? single.Name : "torrent";
            var sanitized = SanitizeFileName(name);
            return string.IsNullOrWhiteSpace(sanitized) ? "package.leecharr" : $"{sanitized}.leecharr";
        }

        return "package.leecharr";
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (!InvalidFileNameChars.Contains(ch) && !char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }

    private List<int> ParseTorrentIds(string queryString, StringValues queryValues)
    {
        var idList = new List<int>();

        if (!string.IsNullOrWhiteSpace(queryString))
        {
            var tokens = queryString.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (int.TryParse(token.Trim(), out var id) && id > 0)
                {
                    idList.Add(id);
                }
            }
        }

        foreach (var val in queryValues)
        {
            if (string.IsNullOrWhiteSpace(val))
            {
                continue;
            }

            var parts = val.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out var parsedId) && parsedId > 0 && !idList.Contains(parsedId))
                {
                    idList.Add(parsedId);
                }
            }
        }

        return idList.Distinct().ToList();
    }
}
