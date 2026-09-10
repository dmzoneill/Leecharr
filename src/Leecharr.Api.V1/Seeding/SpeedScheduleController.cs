// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Bandwidth;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Seeding;

[V1ApiController("speedschedule")]
public class SpeedScheduleController : RestControllerWithSignalR<SpeedScheduleResource, SpeedSchedule>
{
    private readonly ISpeedScheduleRepository speedScheduleRepository;
    private readonly ISpeedSchedulerService speedSchedulerService;

    public SpeedScheduleController(
        ISpeedScheduleRepository speedScheduleRepository,
        ISpeedSchedulerService speedSchedulerService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        this.speedScheduleRepository = speedScheduleRepository;
        this.speedSchedulerService = speedSchedulerService;
    }

    [HttpGet]
    public ActionResult<List<SpeedScheduleResource>> GetAll()
    {
        return this.Ok(this.speedScheduleRepository.All().Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<SpeedScheduleResource> GetById(int id)
    {
        var schedule = this.speedScheduleRepository.Get(id);
        if (schedule == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(schedule));
    }

    [HttpGet("active")]
    public ActionResult<SpeedLimitsResource> GetActiveLimits()
    {
        var limits = this.speedSchedulerService.GetCurrentLimits();
        return this.Ok(new SpeedLimitsResource
        {
            MaxDownloadSpeedKbps = limits.MaxDownloadSpeedKbps,
            MaxUploadSpeedKbps = limits.MaxUploadSpeedKbps,
            IsThrottled = limits.IsThrottled,
            IsPaused = limits.IsPaused,
            IsDownloadPaused = limits.IsDownloadPaused,
            IsUploadPaused = limits.IsUploadPaused,
        });
    }

    [HttpPost]
    public async Task<ActionResult<SpeedScheduleResource>> Create([FromBody] SpeedScheduleResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Schedule name is required.");
        }

        if (resource.MaxDownloadSpeed < 0 || resource.MaxUploadSpeed < 0)
        {
            return this.BadRequest("Speed limits must be non-negative.");
        }

        if (!TimeOnly.TryParse(resource.StartTime, CultureInfo.InvariantCulture, out _) ||
            !TimeOnly.TryParse(resource.EndTime, CultureInfo.InvariantCulture, out _))
        {
            return this.BadRequest("StartTime and EndTime must be valid times.");
        }

        resource.Name = resource.Name.Trim();
        var model = ToModel(resource);
        var created = this.speedScheduleRepository.Insert(model);
        await this.speedSchedulerService.ApplyCurrentLimitsAsync();
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SpeedScheduleResource>> Update(int id, [FromBody] SpeedScheduleResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Schedule name is required.");
        }

        if (resource.MaxDownloadSpeed < 0 || resource.MaxUploadSpeed < 0)
        {
            return this.BadRequest("Speed limits must be non-negative.");
        }

        if (!TimeOnly.TryParse(resource.StartTime, CultureInfo.InvariantCulture, out _) ||
            !TimeOnly.TryParse(resource.EndTime, CultureInfo.InvariantCulture, out _))
        {
            return this.BadRequest("StartTime and EndTime must be valid times.");
        }

        var existing = this.speedScheduleRepository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        resource.Name = resource.Name.Trim();
        var model = ToModel(resource);
        model.Id = id;
        this.speedScheduleRepository.Update(model);
        await this.speedSchedulerService.ApplyCurrentLimitsAsync();
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        this.speedScheduleRepository.Delete(id);
        await this.speedSchedulerService.ApplyCurrentLimitsAsync();
        return this.Ok();
    }

    protected override SpeedScheduleResource GetResourceById(SpeedSchedule model)
    {
        return ToResource(model);
    }

    private static SpeedScheduleResource ToResource(SpeedSchedule s)
    {
        if (s == null)
        {
            return null;
        }

        return new SpeedScheduleResource
        {
            Id = s.Id,
            Name = s.Name,
            Days = s.Days,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            MaxDownloadSpeed = s.MaxDownloadSpeed,
            MaxUploadSpeed = s.MaxUploadSpeed,
            IsEnabled = s.IsEnabled,
            Priority = s.Priority,
        };
    }

    private static SpeedSchedule ToModel(SpeedScheduleResource r)
    {
        return new SpeedSchedule
        {
            Id = r.Id,
            Name = r.Name,
            Days = r.Days,
            StartTime = r.StartTime ?? "00:00:00",
            EndTime = r.EndTime ?? "23:59:59",
            MaxDownloadSpeed = r.MaxDownloadSpeed,
            MaxUploadSpeed = r.MaxUploadSpeed,
            IsEnabled = r.IsEnabled,
            Priority = r.Priority,
        };
    }
}
