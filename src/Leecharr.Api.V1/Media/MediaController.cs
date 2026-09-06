// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Api.V1.Media;

[V1ApiController("media")]
public class MediaController : RestController<MediaMetadataResource>
{
    private readonly IMediaEnrichmentService mediaEnrichmentService;

    public MediaController(IMediaEnrichmentService mediaEnrichmentService)
    {
        this.mediaEnrichmentService = mediaEnrichmentService;
    }

    [HttpGet]
    public ActionResult<List<MediaMetadataResource>> GetAll()
    {
        var all = this.mediaEnrichmentService.GetAllMetadata();
        return this.Ok(all.Values.Select(MediaMetadataResourceMapper.ToResource).ToList());
    }

    [HttpDelete("{torrentId:int}")]
    public IActionResult Delete(int torrentId)
    {
        this.mediaEnrichmentService.DeleteMetadata(torrentId);
        return this.NoContent();
    }

    [HttpGet("{torrentId:int}")]
    public ActionResult<MediaMetadataResource> GetByTorrentId(int torrentId)
    {
        var meta = this.mediaEnrichmentService.GetMetadata(torrentId);
        if (meta == null)
        {
            return this.NotFound();
        }

        return this.Ok(MediaMetadataResourceMapper.ToResource(meta));
    }

    [HttpGet("artwork/{torrentId:int}/{type}")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    public ActionResult GetArtwork(int torrentId, string type)
    {
        if (!string.Equals(type, "poster", global::System.StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "backdrop", global::System.StringComparison.OrdinalIgnoreCase))
        {
            return this.NotFound();
        }

        var meta = this.mediaEnrichmentService.GetMetadata(torrentId);
        if (meta == null)
        {
            return this.NotFound();
        }

        var path = string.Equals(type, "poster", global::System.StringComparison.OrdinalIgnoreCase)
            ? meta.PosterLocalPath
            : meta.BackdropLocalPath;

        if (string.IsNullOrEmpty(path) || path.Contains("..") || !global::System.IO.File.Exists(path))
        {
            return this.NotFound();
        }

        var ext = global::System.IO.Path.GetExtension(path).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };

        return this.PhysicalFile(global::System.IO.Path.GetFullPath(path), contentType);
    }
}
