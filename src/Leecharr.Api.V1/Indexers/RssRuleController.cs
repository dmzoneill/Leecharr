// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Indexers;

namespace Leecharr.Api.V1.Indexers;

[V1ApiController("rssrules")]
[Route("api/v1/rssrule")]
public class RssRuleController : Controller
{
    private readonly IRssRuleRepository rssRuleRepository;
    private readonly IRssSyncService rssSyncService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public RssRuleController(
        IRssRuleRepository rssRuleRepository,
        IRssSyncService rssSyncService = null)
    {
        this.rssRuleRepository = rssRuleRepository;
        this.rssSyncService = rssSyncService;
    }

    [HttpGet]
    public ActionResult<List<RssRuleResource>> GetAll()
    {
        var rules = this.rssRuleRepository.All();
        return this.Ok(rules.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<RssRuleResource> Get(int id)
    {
        var rule = this.rssRuleRepository.Get(id);
        if (rule == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(rule));
    }

    [HttpPost]
    public ActionResult<RssRuleResource> Create([FromBody] RssRuleResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.rssRuleRepository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<RssRuleResource> Update(int id, [FromBody] RssRuleResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.rssRuleRepository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        this.rssRuleRepository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpPut]
    public ActionResult<RssRuleResource> UpdateWithoutId([FromBody] RssRuleResource resource)
    {
        if (resource == null || resource.Id <= 0)
        {
            return this.BadRequest();
        }

        return this.Update(resource.Id, resource);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.rssRuleRepository.Delete(id);
        return this.Ok();
    }

    [HttpPost("sync")]
    [HttpPost("sync-rss")]
    public async Task<ActionResult<object>> SyncRss()
    {
        if (this.rssSyncService == null)
        {
            return this.BadRequest(new { success = false, message = "RSS sync service is not configured." });
        }

        try
        {
            var count = await this.rssSyncService.SyncRssFeedsAsync();
            return this.Ok(new { success = true, grabbedCount = count });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to run RSS sync");
            return this.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static RssRuleResource ToResource(RssRule model)
    {
        return new RssRuleResource
        {
            Id = model.Id,
            Name = model.Name,
            IsEnabled = model.IsEnabled,
            MustContain = model.MustContain,
            MustNotContain = model.MustNotContain,
            MinSeeders = model.MinSeeders,
            MinSizeBytes = model.MinSizeBytes,
            MaxSizeBytes = model.MaxSizeBytes,
            FreeleechOnly = model.FreeleechOnly,
            CategoryId = model.CategoryId,
            IndexerIds = model.IndexerIds ?? new List<int>(),
        };
    }

    private static RssRule ToModel(RssRuleResource resource)
    {
        return new RssRule
        {
            Id = resource.Id,
            Name = resource.Name,
            IsEnabled = resource.IsEnabled,
            MustContain = resource.MustContain,
            MustNotContain = resource.MustNotContain,
            MinSeeders = resource.MinSeeders,
            MinSizeBytes = resource.MinSizeBytes,
            MaxSizeBytes = resource.MaxSizeBytes,
            FreeleechOnly = resource.FreeleechOnly,
            CategoryId = resource.CategoryId,
            IndexerIds = resource.IndexerIds ?? new List<int>(),
        };
    }
}
