using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Bandwidth;

namespace Leecharr.Api.V1.Seeding;

[V1ApiController("speedschedule")]
public class SpeedScheduleController : Controller
{
    private readonly ISpeedScheduleRepository _speedScheduleRepository;
    private readonly ISpeedSchedulerService _speedSchedulerService;

    public SpeedScheduleController(
        ISpeedScheduleRepository speedScheduleRepository,
        ISpeedSchedulerService speedSchedulerService)
    {
        _speedScheduleRepository = speedScheduleRepository;
        _speedSchedulerService = speedSchedulerService;
    }

    [HttpGet]
    public ActionResult<List<SpeedScheduleResource>> GetAll()
    {
        return Ok(_speedScheduleRepository.All().Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<SpeedScheduleResource> GetById(int id)
    {
        var schedule = _speedScheduleRepository.Get(id);
        if (schedule == null)
        {
            return NotFound();
        }

        return Ok(ToResource(schedule));
    }

    [HttpGet("active")]
    public ActionResult<SpeedLimitsResource> GetActiveLimits()
    {
        var limits = _speedSchedulerService.GetCurrentLimits();
        return Ok(new SpeedLimitsResource
        {
            MaxDownloadSpeedKbps = limits.MaxDownloadSpeedKbps,
            MaxUploadSpeedKbps = limits.MaxUploadSpeedKbps,
            IsThrottled = limits.IsThrottled,
            IsPaused = limits.IsPaused
        });
    }

    [HttpPost]
    public ActionResult<SpeedScheduleResource> Create([FromBody] SpeedScheduleResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        var created = _speedScheduleRepository.Insert(model);
        return Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<SpeedScheduleResource> Update(int id, [FromBody] SpeedScheduleResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var existing = _speedScheduleRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        _speedScheduleRepository.Update(model);
        return Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _speedScheduleRepository.Delete(id);
        return Ok();
    }

    private static SpeedScheduleResource ToResource(SpeedSchedule s)
    {
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
            Priority = s.Priority
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
            Priority = r.Priority
        };
    }
}
