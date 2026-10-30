// Copyright (c) FeedItOut. All rights reserved.

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
    private readonly IBlocklistSyncState syncState;

    public BlocklistController(
        IConfigService configService,
        IBlocklistService blocklistService,
        IBlocklistUpdateService updateService,
        IBlocklistSyncState syncState)
    {
        this.configService = configService;
        this.blocklistService = blocklistService;
        this.updateService = updateService;
        this.syncState = syncState;
    }

    [HttpGet]
    public ActionResult<BlocklistResource> GetBlocklist()
    {
        return this.Ok(this.BuildResource());
    }

    [HttpPut] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
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

        if (request.AutoUpdateEnabled.HasValue)
        {
            updates["BlocklistAutoUpdateEnabled"] = request.AutoUpdateEnabled.Value;
        }

        if (request.AutoUpdateIntervalHours.HasValue && request.AutoUpdateIntervalHours.Value > 0)
        {
            updates["BlocklistUpdateIntervalHours"] = request.AutoUpdateIntervalHours.Value;
        }
        else if (request.AutoUpdateIntervalDays.HasValue && request.AutoUpdateIntervalDays.Value > 0)
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
            if (!this.configService.BlocklistEnabled)
            {
                var existingRules = this.blocklistService.TotalRulesLoaded;
                return this.Ok(new BlocklistSyncResponse
                {
                    Success = true,
                    Status = "Skipped",
                    Message = "Blocklist update skipped: blocklist is disabled in configuration.",
                    RuleCount = existingRules,
                    TotalRuleCount = existingRules,
                    LastUpdatedUtc = this.syncState.LastSuccessfulSyncUtc,
                });
            }

            var rulesLoaded = await this.updateService.UpdateRulesAsync(cancellationToken);
            var lastUpdatedUtc = this.syncState.LastSuccessfulSyncUtc;
            if (rulesLoaded > 0)
            {
                var syncedAt = DateTime.UtcNow;
                this.syncState.RecordSuccessfulSync(syncedAt);
                lastUpdatedUtc = syncedAt;
            }

            return this.Ok(new BlocklistSyncResponse
            {
                Success = rulesLoaded > 0,
                Status = rulesLoaded > 0 ? "Success" : "Warning",
                Message = rulesLoaded > 0
                    ? $"Blocklist synchronized: {rulesLoaded:N0} rules active."
                    : "No rules could be loaded from configured sources.",
                RuleCount = rulesLoaded > 0 ? rulesLoaded : this.blocklistService.TotalRulesLoaded,
                TotalRuleCount = rulesLoaded > 0 ? rulesLoaded : this.blocklistService.TotalRulesLoaded,
                LastUpdatedUtc = lastUpdatedUtc,
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
                LastUpdatedUtc = this.syncState.LastSuccessfulSyncUtc,
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
        var ipv4Rules = this.blocklistService.Ipv4RulesLoaded;
        var ipv6Rules = this.blocklistService.Ipv6RulesLoaded;
        var intervalHours = Math.Max(1, this.configService.BlocklistUpdateIntervalHours);
        var intervalDays = intervalHours % 24 == 0
            ? intervalHours / 24
            : Math.Max(1, intervalHours / 24);

        return new BlocklistResource
        {
            Enabled = this.configService.BlocklistEnabled,
            Url = this.configService.BlocklistUrl,
            AutoUpdateEnabled = this.configService.BlocklistAutoUpdateEnabled,
            AutoUpdateIntervalHours = intervalHours,
            AutoUpdateIntervalDays = intervalDays,
            Ipv4RuleCount = ipv4Rules,
            Ipv6RuleCount = ipv6Rules,
            TotalRuleCount = totalRules,
            RuleCount = totalRules,
            LastUpdatedUtc = this.syncState.LastSuccessfulSyncUtc,
            LastSyncStatus = totalRules > 0 ? "Active" : "Idle",
            NextScheduledSyncUtc = this.syncState.ResolveNextScheduledSyncUtc(
                intervalHours,
                this.configService.BlocklistAutoUpdateEnabled),
        };
    }
}
