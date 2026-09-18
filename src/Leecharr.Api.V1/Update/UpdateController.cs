// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;

namespace Leecharr.Api.V1.Update;

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
        var isContainer = OsInfo.IsContainer;
        var resources = packages.Select(p => new UpdateResource
        {
            Id = id++,
            Version = p.Version,
            ReleaseDate = p.ReleaseDate,
            FileName = p.FileName,
            Url = p.Url,
            Installed = p.Installed,
            Latest = p.Latest,
            IsContainer = isContainer,
            Changes = new UpdateChangesResource
            {
                New = p.Changes?.New ?? new List<string>(),
                Fixed = p.Changes?.Fixed ?? new List<string>(),
            },
        }).ToList();

        return this.Ok(resources);
    }
}
