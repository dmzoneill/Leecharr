// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Api.V1.Blocklist;

[V1ApiController("blocklist")]
[Authorize(Policy = "RequireOperator")]
public class BlocklistController : Controller
{
    private readonly IConfigService configService;
    private readonly IBlocklistService blocklistService;
    private readonly IBlocklistUpdateService updateService;

    public BlocklistController(
        IConfigService configService,
        IBlocklistService blocklistService,
        IBlocklistUpdateService updateService)
    {
        this.configService = configService;
        this.blocklistService = blocklistService;
        this.updateService = updateService;
    }

    [HttpGet]
    [AllowAnonymous]
    public ActionResult<BlocklistResource> GetBlocklist()
    {
        return this.Ok(this.BuildResource());
    }

    [HttpPut]
    public ActionResult<BlocklistResource> UpdateBlocklist([FromBody] BlocklistConfigRequest request)
    {
        if (request == null)
        {
            return this.BadRequest("Request body cannot be empty.");
        }

        var updates = new Dictionary<string, object>();

        if (request.Enabled.HasValue)
        {
            updates["BlocklistEnabled"] = request.Enabled.Value;
        }

        if (request.Url != null)
        {
            updates["BlocklistUrl"] = request.Url.Trim();
        }

        if (request.AutoUpdateIntervalDays.HasValue && request.AutoUpdateIntervalDays.Value > 0)
        {
            updates["BlocklistUpdateIntervalHours"] = request.AutoUpdateIntervalDays.Value * 24;
        }

        if (updates.Count > 0)
        {
            this.configService.SaveConfigDictionary(updates);
        }

        return this.Ok(this.BuildResource());
    }

    [HttpPost("sync")]
    public async Task<ActionResult<BlocklistSyncResponse>> SyncBlocklistAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rulesLoaded = await this.updateService.UpdateRulesAsync(cancellationToken);
            return this.Ok(new BlocklistSyncResponse
            {
                Success = rulesLoaded > 0,
                Status = rulesLoaded > 0 ? "Success" : "Warning",
                Message = rulesLoaded > 0
                    ? $"Blocklist synchronized: {rulesLoaded:N0} rules active."
                    : "No rules could be loaded from configured sources.",
                RuleCount = rulesLoaded > 0 ? rulesLoaded : this.blocklistService.TotalRulesLoaded,
                TotalRuleCount = rulesLoaded > 0 ? rulesLoaded : this.blocklistService.TotalRulesLoaded,
                LastUpdatedUtc = DateTime.UtcNow,
            });
        }
        catch (Exception ex)
        {
            return this.Ok(new BlocklistSyncResponse
            {
                Success = false,
                Status = "Error",
                Message = $"Failed to synchronize blocklist: {ex.Message}",
                RuleCount = this.blocklistService.TotalRulesLoaded,
                TotalRuleCount = this.blocklistService.TotalRulesLoaded,
                LastUpdatedUtc = DateTime.UtcNow,
            });
        }
    }

    [HttpPost("test")]
    public ActionResult<BlocklistTestResponse> TestIp([FromBody] BlocklistTestRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Ip))
        {
            return this.BadRequest(new { message = "IP address is required." });
        }

        if (!IPAddress.TryParse(request.Ip.Trim(), out var address))
        {
            return this.BadRequest(new { message = "Invalid IP address format." });
        }

        var isBlocked = this.blocklistService.IsIpBlocked(address.ToString());

        return this.Ok(new BlocklistTestResponse
        {
            IsBlocked = isBlocked,
            Rule = isBlocked ? "Active blocklist rule" : null,
        });
    }

    private BlocklistResource BuildResource()
    {
        var totalRules = this.blocklistService.TotalRulesLoaded;
        var intervalHours = this.configService.BlocklistUpdateIntervalHours;
        var intervalDays = Math.Max(1, intervalHours / 24);

        return new BlocklistResource
        {
            Enabled = this.configService.BlocklistEnabled,
            Url = this.configService.BlocklistUrl,
            AutoUpdateEnabled = this.configService.BlocklistEnabled,
            AutoUpdateIntervalDays = intervalDays,
            Ipv4RuleCount = totalRules,
            Ipv6RuleCount = 0,
            TotalRuleCount = totalRules,
            RuleCount = totalRules,
            LastUpdatedUtc = DateTime.UtcNow,
            LastSyncStatus = totalRules > 0 ? "Active" : "Idle",
            NextScheduledSyncUtc = DateTime.UtcNow.AddHours(intervalHours),
        };
    }
}
