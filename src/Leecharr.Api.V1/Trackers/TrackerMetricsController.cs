// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Trackers.Metrics;

namespace Leecharr.Api.V1.Trackers;

[V1ApiController("trackermetrics")]
public class TrackerMetricsController : Controller
{
    private readonly ITrackerMetricService trackerMetricService;

    public TrackerMetricsController(ITrackerMetricService trackerMetricService)
    {
        this.trackerMetricService = trackerMetricService;
    }

    [HttpGet]
    public ActionResult<List<TrackerMetricResource>> GetAll()
    {
        var metrics = this.trackerMetricService.GetAllMetrics();
        return this.Ok(metrics.Select(TrackerMetricResourceMapper.ToResource).ToList());
    }

    [HttpGet("summary")]
    public ActionResult<TrackerMetricsSummary> GetSummary()
    {
        var summary = this.trackerMetricService.GetSummary();
        return this.Ok(summary);
    }

    [HttpGet("{id:int}")]
    public ActionResult<TrackerMetricResource> Get(int id)
    {
        var metric = this.trackerMetricService.GetMetric(id);
        if (metric == null)
        {
            return this.NotFound();
        }

        return this.Ok(TrackerMetricResourceMapper.ToResource(metric));
    }

    [HttpGet("{id:int}/history")]
    public ActionResult<List<TrackerMetricSnapshot>> GetHistory(int id, [FromQuery] int hours = 24, [FromQuery] int limit = 500)
    {
        if (hours <= 0)
        {
            return this.BadRequest("Hours must be greater than 0.");
        }

        if (hours > 168)
        {
            return this.BadRequest("Hours cannot exceed 168 (7 days).");
        }

        if (limit <= 0 || limit > 2000)
        {
            return this.BadRequest("Limit must be between 1 and 2000.");
        }

        var history = this.trackerMetricService.GetHistory(id, hours, limit);
        return this.Ok(history);
    }

    [HttpPost("{id:int}/reset")]
    public ActionResult Reset(int id)
    {
        this.trackerMetricService.ResetMetrics(id);
        return this.Ok(new { success = true, message = "Tracker metrics reset." });
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.trackerMetricService.DeleteMetric(id);
        return this.Ok(new { success = true, message = "Tracker metric deleted." });
    }
}
