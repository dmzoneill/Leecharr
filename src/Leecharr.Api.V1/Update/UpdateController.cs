// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Update;

namespace Leecharr.Api.V1.Update;

public class UpdateChangesResource
{
    public List<string> New { get; set; } = new();

    public List<string> Fixed { get; set; } = new();
}

public class UpdateResource : RestResource
{
    public string Version { get; set; }

    public DateTime ReleaseDate { get; set; }

    public string FileName { get; set; }

    public string Url { get; set; }

    public bool Installed { get; set; }

    public bool Latest { get; set; }

    public UpdateChangesResource Changes { get; set; } = new();
}

[V1ApiController("update")]
public class UpdateController : Controller
{
    private readonly IUpdateCheckService updateCheckService;

    public UpdateController(IUpdateCheckService updateCheckService = null)
    {
        this.updateCheckService = updateCheckService;
    }

    [HttpGet]
    public async Task<ActionResult<List<UpdateResource>>> GetUpdates(CancellationToken cancellationToken = default)
    {
        var service = this.updateCheckService ?? new UpdateCheckService();
        var packages = await service.GetAvailableUpdatesAsync(cancellationToken).ConfigureAwait(false);

        var id = 1;
        var resources = packages.Select(p => new UpdateResource
        {
            Id = id++,
            Version = p.Version,
            ReleaseDate = p.ReleaseDate,
            FileName = p.FileName,
            Url = p.Url,
            Installed = p.Installed,
            Latest = p.Latest,
            Changes = new UpdateChangesResource
            {
                New = p.Changes?.New ?? new List<string>(),
                Fixed = p.Changes?.Fixed ?? new List<string>(),
            },
        }).ToList();

        return this.Ok(resources);
    }
}
