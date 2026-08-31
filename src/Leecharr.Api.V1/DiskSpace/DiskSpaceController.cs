// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IDiskSpaceService diskSpaceService;

    public DiskSpaceController(IDiskSpaceService diskSpaceService)
    {
        this.diskSpaceService = diskSpaceService;
    }

    [HttpGet]
    public ActionResult<List<DiskSpaceResource>> GetDiskSpace()
    {
        var diskSpace = this.diskSpaceService.GetDiskSpace();
        var resources = diskSpace.Select((d, idx) => new DiskSpaceResource
        {
            Id = idx + 1,
            Path = d.Path,
            Label = d.Label,
            FreeSpace = d.FreeSpace,
            TotalSpace = d.TotalSpace,
        }).ToList();

        return this.Ok(resources);
    }
}
