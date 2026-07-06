using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using Leecharr.Http.REST;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Api.V1.Media;

[V1ApiController("media")]
public class MediaController : RestController<MediaMetadataResource>
{
    private readonly IMediaEnrichmentService _mediaEnrichmentService;

    public MediaController(IMediaEnrichmentService mediaEnrichmentService)
    {
        _mediaEnrichmentService = mediaEnrichmentService;
    }

    [HttpGet("{torrentId:int}")]
    public ActionResult<MediaMetadataResource> GetByTorrentId(int torrentId)
    {
        var meta = _mediaEnrichmentService.GetMetadata(torrentId);
        if (meta == null)
        {
            return NotFound();
        }

        return Ok(MediaMetadataResourceMapper.ToResource(meta));
    }

    [HttpGet("artwork/{torrentId:int}/{type}")]
    public ActionResult GetArtwork(int torrentId, string type)
    {
        var meta = _mediaEnrichmentService.GetMetadata(torrentId);
        if (meta == null)
        {
            return NotFound();
        }

        var path = string.Equals(type, "poster", global::System.StringComparison.OrdinalIgnoreCase)
            ? meta.PosterLocalPath
            : meta.BackdropLocalPath;

        if (string.IsNullOrEmpty(path) || !global::System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, "image/jpeg");
    }
}
