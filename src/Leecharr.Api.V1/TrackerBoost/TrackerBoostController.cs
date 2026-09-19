// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.TrackerBoost;

namespace Leecharr.Api.V1.TrackerBoost;

[V1ApiController("trackerboost")]
[Route("api/v1/downloadplusplus")]
[Authorize(Policy = "RequireOperator")]
public class TrackerBoostController : Controller
{
    private readonly ITrackerBoostService trackerBoostService;

    public TrackerBoostController(ITrackerBoostService trackerBoostService)
    {
        this.trackerBoostService = trackerBoostService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var summary = await this.trackerBoostService.GetStatusSummaryAsync();
        return this.Ok(summary);
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var settings = this.trackerBoostService.GetSettings();
        return this.Ok(settings);
    }

    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] TrackerBoostSettings settings)
    {
        if (settings == null)
        {
            return this.BadRequest(new { message = "Invalid settings" });
        }

        this.trackerBoostService.UpdateSettings(settings);
        return this.Ok(this.trackerBoostService.GetSettings());
    }

    [HttpGet("trackers")]
    public IActionResult GetTrackers()
    {
        var trackers = this.trackerBoostService.GetAllTrackers();
        return this.Ok(trackers);
    }

    [HttpGet("matrix")]
    public async Task<IActionResult> GetCrossMatrix()
    {
        var matrix = await this.trackerBoostService.GetCrossMatrixAsync();
        return this.Ok(matrix);
    }

    [HttpGet("check/{torrentId:int}")]
    public async Task<IActionResult> InspectTorrentTrackers(int torrentId)
    {
        var result = await this.trackerBoostService.InspectTorrentTrackersAsync(torrentId);
        return this.Ok(result);
    }

    [HttpGet("check-hash/{infoHash}")]
    public async Task<IActionResult> InspectHashTrackers(string infoHash, [FromQuery] string name = "")
    {
        var result = await this.trackerBoostService.InspectHashTrackersAsync(infoHash, name);
        return this.Ok(result);
    }

    [HttpPost("trackers")]
    public IActionResult AddTracker([FromBody] AddTrackerResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Url))
        {
            return this.BadRequest(new { message = "Tracker URL is required." });
        }

        var tracker = this.trackerBoostService.AddTracker(resource.Url, TrackerSourceType.Manual, "Manual Entry");
        return this.Ok(tracker);
    }

    [HttpDelete("trackers/{id:int}")]
    public IActionResult DeleteTracker(int id)
    {
        this.trackerBoostService.DeleteTracker(id);
        return this.Ok(new { success = true });
    }

    [HttpPost("trackers/bulk")]
    public IActionResult BulkImportTrackers([FromBody] BulkImportTrackersResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.TrackersText))
        {
            return this.BadRequest(new { message = "Trackers text is required." });
        }

        var imported = 0;
        using var reader = new StringReader(resource.TrackersText);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var clean = line.Trim();
            if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith("#"))
            {
                continue;
            }

            if (TrackerBoostService.IsValidPublicTrackerUrl(clean))
            {
                this.trackerBoostService.AddTracker(clean, TrackerSourceType.Manual, "Bulk Import");
                imported++;
            }
        }

        return this.Ok(new { success = true, importedCount = imported });
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanTrackers()
    {
        var testedCount = await this.trackerBoostService.ProbeTrackerHealthAsync();
        return this.Ok(new { success = true, testedCount });
    }

    [HttpPost("harvest/downloads")]
    public async Task<IActionResult> HarvestFromDownloads()
    {
        var count = await this.trackerBoostService.HarvestFromActiveDownloadsAsync();
        return this.Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("harvest/prowlarr")]
    public async Task<IActionResult> HarvestProwlarr()
    {
        var count = await this.trackerBoostService.HarvestFromProwlarrAsync();
        return this.Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("harvest/feeds")]
    public async Task<IActionResult> HarvestFeeds()
    {
        var count = await this.trackerBoostService.HarvestFromCuratedListsAsync();
        return this.Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("boost/{torrentId:int}")]
    public async Task<IActionResult> BoostTorrent(int torrentId, [FromQuery] bool onlyVerified = true)
    {
        var result = await this.trackerBoostService.BoostTorrentAsync(torrentId, onlyVerified);
        return this.Ok(result);
    }

    [HttpPost("boost-hash/{infoHash}")]
    public async Task<IActionResult> BoostHash(string infoHash, [FromQuery] string name = "", [FromQuery] bool onlyVerified = true)
    {
        var result = await this.trackerBoostService.BoostHashAsync(infoHash, name, onlyVerified);
        return this.Ok(result);
    }

    [HttpPost("inject")]
    public async Task<IActionResult> InjectTracker([FromBody] InjectTrackerResource resource)
    {
        if (resource == null || (resource.TorrentId <= 0 && string.IsNullOrWhiteSpace(resource.InfoHash)) || string.IsNullOrWhiteSpace(resource.TrackerUrl))
        {
            return this.BadRequest(new { message = "TorrentId or InfoHash and TrackerUrl are required." });
        }

        if (resource.TorrentId > 0)
        {
            var result = await this.trackerBoostService.InjectTrackerToTorrentAsync(resource.TorrentId, resource.TrackerUrl, resource.Force);
            return this.Ok(result);
        }
        else
        {
            var result = await this.trackerBoostService.InjectTrackerToHashAsync(resource.InfoHash, resource.TrackerUrl, resource.Force);
            return this.Ok(result);
        }
    }

    [HttpPost("boost-all")]
    public async Task<IActionResult> BoostAllTorrents([FromQuery] bool onlyVerified = true)
    {
        var results = await this.trackerBoostService.BoostAllTorrentsAsync(onlyVerified);
        return this.Ok(results);
    }

    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int limit = 100, [FromQuery] string category = null, [FromQuery] string level = null)
    {
        var logs = this.trackerBoostService.GetLogs(limit, category, level);
        return this.Ok(logs);
    }

    [HttpDelete("logs")]
    public IActionResult ClearLogs()
    {
        this.trackerBoostService.ClearLogs();
        return this.Ok(new { success = true });
    }
}
