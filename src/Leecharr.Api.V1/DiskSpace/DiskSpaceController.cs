using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using Leecharr.Http.REST;
using NzbDrone.Core.DiskSpace;

namespace Leecharr.Api.V1.DiskSpace;

public class DiskSpaceResource : RestResource
{
    public string Path { get; set; }
    public string Label { get; set; }
    public long FreeSpace { get; set; }
    public long TotalSpace { get; set; }
}

[V1ApiController("diskspace")]
public class DiskSpaceController : Controller
{
    private readonly IDiskSpaceService _diskSpaceService;

    public DiskSpaceController(IDiskSpaceService diskSpaceService)
    {
        _diskSpaceService = diskSpaceService;
    }

    [HttpGet]
    public ActionResult<List<DiskSpaceResource>> GetDiskSpace()
    {
        var diskSpace = _diskSpaceService.GetDiskSpace();
        var resources = diskSpace.Select((d, idx) => new DiskSpaceResource
        {
            Id = idx + 1,
            Path = d.Path,
            Label = d.Label,
            FreeSpace = d.FreeSpace,
            TotalSpace = d.TotalSpace
        }).ToList();

        return Ok(resources);
    }
}
