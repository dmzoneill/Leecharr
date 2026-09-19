// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Hosting;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;

namespace Leecharr.Api.V1.Update;

[V1ApiController("update")]
[Authorize(Policy = "RequireAdmin")]
public class UpdateController : Controller
{
    private readonly IUpdateCheckService updateCheckService;
    private readonly IRuntimeInfo runtimeInfo;
    private readonly IHostApplicationLifetime hostApplicationLifetime;

    public UpdateController(
        IUpdateCheckService updateCheckService = null,
        IRuntimeInfo runtimeInfo = null,
        IHostApplicationLifetime hostApplicationLifetime = null)
    {
        this.updateCheckService = updateCheckService;
        this.runtimeInfo = runtimeInfo;
        this.hostApplicationLifetime = hostApplicationLifetime;
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

    [HttpPost]
    public async Task<ActionResult> InstallUpdate([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateResource package = null, CancellationToken cancellationToken = default)
    {
        var service = this.updateCheckService ?? new UpdateCheckService();
        var packages = await service.GetAvailableUpdatesAsync(cancellationToken).ConfigureAwait(false);

        var targetPackage = package != null && !string.IsNullOrWhiteSpace(package.Version)
            ? packages?.FirstOrDefault(u => string.Equals(u.Version, package.Version, StringComparison.OrdinalIgnoreCase))
            : packages?.FirstOrDefault(u => u.Latest || !u.Installed);

        if (this.runtimeInfo != null)
        {
            this.runtimeInfo.RestartPending = true;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            this.hostApplicationLifetime?.StopApplication();
        });

        return this.Ok(new
        {
            message = "Update initiated. Restarting Leecharr...",
            version = targetPackage?.Version ?? package?.Version ?? BuildInfo.Version?.ToString(),
            restartPending = true,
        });
    }
}
