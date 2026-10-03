// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Api.V1.Media;

[V1ApiController("media")]
public class MediaController : RestController<MediaMetadataResource>
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg"];
    private static readonly string[] FanartFallbacks = ["backdrop", "background"];
    private static readonly string[] BackdropFallbacks = ["fanart", "background"];
    private static readonly string[] PosterFallbacks = ["cover", "folder", "thumb"];
    private static readonly string[] ThumbFallbacks = ["poster", "cover", "folder"];
    private static readonly string[] BannerFallbacks = ["season-banner", "fanart", "backdrop"];

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
    [Authorize(Policy = "RequireOperator")]
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

    private static readonly HashSet<string> ValidArtworkTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "poster",
        "backdrop",
        "banner",
        "fanart",
        "logo",
        "clearart",
        "thumb",
        "screenshot",
    };

    [HttpGet("artwork/{torrentId:int}/{type}")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    public ActionResult GetArtwork(int torrentId, string type)
    {
        if (string.IsNullOrWhiteSpace(type) || !ValidArtworkTypes.Contains(type))
        {
            return this.NotFound();
        }

        var meta = this.mediaEnrichmentService.GetMetadata(torrentId);
        if (meta == null)
        {
            return this.NotFound();
        }

        string path = null;
        if (string.Equals(type, "poster", StringComparison.OrdinalIgnoreCase))
        {
            path = meta.PosterLocalPath;
        }
        else if (string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "fanart", StringComparison.OrdinalIgnoreCase))
        {
            path = meta.BackdropLocalPath;
        }
        else if (string.Equals(type, "thumb", StringComparison.OrdinalIgnoreCase))
        {
            path = meta.PosterLocalPath;
        }

        if (string.IsNullOrEmpty(path) || !global::System.IO.File.Exists(path))
        {
            path = FindArtworkOnDisk(meta, type);
        }

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

    private static string FindArtworkOnDisk(TorrentMediaMetadata meta, string type)
    {
        var candidateDirs = new List<string>();
        if (!string.IsNullOrEmpty(meta.PosterLocalPath))
        {
            var dir = global::System.IO.Path.GetDirectoryName(meta.PosterLocalPath);
            if (!string.IsNullOrEmpty(dir) && global::System.IO.Directory.Exists(dir))
            {
                candidateDirs.Add(dir);
            }
        }

        if (!string.IsNullOrEmpty(meta.BackdropLocalPath))
        {
            var dir = global::System.IO.Path.GetDirectoryName(meta.BackdropLocalPath);
            if (!string.IsNullOrEmpty(dir) && global::System.IO.Directory.Exists(dir) && !candidateDirs.Contains(dir))
            {
                candidateDirs.Add(dir);
            }
        }

        var candidateNames = new List<string> { type };

        if (string.Equals(type, "fanart", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(FanartFallbacks);
        }
        else if (string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(BackdropFallbacks);
        }
        else if (string.Equals(type, "poster", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(PosterFallbacks);
        }
        else if (string.Equals(type, "thumb", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(ThumbFallbacks);
        }
        else if (string.Equals(type, "banner", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(BannerFallbacks);
        }

        foreach (var dir in candidateDirs)
        {
            foreach (var name in candidateNames)
            {
                foreach (var ext in ImageExtensions)
                {
                    var file = global::System.IO.Path.Combine(dir, $"{name}{ext}");
                    if (global::System.IO.File.Exists(file))
                    {
                        return file;
                    }
                }
            }
        }

        return null;
    }
}
