// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Diagnostics.CodeAnalysis;
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

        return this.PhysicalFile(global::System.IO.Path.GetFullPath(path), "image/jpeg");
    }
}
