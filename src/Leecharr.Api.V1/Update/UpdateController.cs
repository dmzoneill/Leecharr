// Copyright (c) FeedItOut. All rights reserved.
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
    private readonly IUpdateInstallService updateInstallService;
    private readonly IRuntimeInfo runtimeInfo;
    private readonly IHostApplicationLifetime hostApplicationLifetime;

    public UpdateController(
        IUpdateCheckService updateCheckService = null,
        IUpdateInstallService updateInstallService = null,
        IRuntimeInfo runtimeInfo = null,
        IHostApplicationLifetime hostApplicationLifetime = null)
    {
        this.updateCheckService = updateCheckService;
        this.updateInstallService = updateInstallService;
        this.runtimeInfo = runtimeInfo;
        this.hostApplicationLifetime = hostApplicationLifetime;
    }

    [HttpGet]
    public async Task<ActionResult<List<UpdateResource>>> GetUpdates(CancellationToken cancellationToken = default)
    {
        var service = this.updateCheckService ?? new UpdateCheckService();
        var packages = await service.GetAvailableUpdatesAsync(cancellationToken).ConfigureAwait(false);
        if (packages == null || packages.Count == 0)
        {
            return this.StatusCode(503, new { message = "Failed to check for updates from GitHub releases." });
        }

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

        if (targetPackage == null)
        {
            return this.BadRequest(new { message = "No matching update package was found." });
        }

        if (targetPackage.Installed)
        {
            return this.BadRequest(new { message = "The selected version is already installed." });
        }

        if (OsInfo.IsContainer)
        {
            return this.BadRequest(new { message = "In-container installs are not supported. Update the container image instead." });
        }

        var installService = this.updateInstallService;
        if (installService == null)
        {
            return this.StatusCode(503, new { message = "Update installation is not available in this host configuration." });
        }

        try
        {
            await installService.PrepareUpdateAsync(targetPackage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return this.StatusCode(500, new { message = $"Failed to prepare update: {ex.Message}" });
        }

        if (this.runtimeInfo != null)
        {
            this.runtimeInfo.RestartPending = true;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(500, this.hostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None);
            this.hostApplicationLifetime?.StopApplication();
        }, this.hostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None);

        return this.Ok(new
        {
            message = "Update downloaded. Restarting Leecharr to apply...",
            version = targetPackage.Version ?? package?.Version ?? BuildInfo.Version?.ToString(),
            restartPending = true,
        });
    }
}
