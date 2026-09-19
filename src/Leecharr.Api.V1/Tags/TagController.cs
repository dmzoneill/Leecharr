// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Tags;

public class TagResource : RestResource
{
    public string Label { get; set; }
}

[V1ApiController("tag")]
[Route("api/v1/tags")]
[Authorize(Policy = "RequireOperator")]
public class TagController : Controller
{
    private readonly ITagRepository tagRepository;
    private readonly ITorrentRepository torrentRepository;
    private readonly INotificationRepository notificationRepository;
    private readonly IIndexerRepository indexerRepository;
    private readonly IAutomationScriptRepository automationScriptRepository;

    public TagController(
        ITagRepository tagRepository,
        ITorrentRepository torrentRepository,
        INotificationRepository notificationRepository,
        IIndexerRepository indexerRepository = null,
        IAutomationScriptRepository automationScriptRepository = null)
    {
        this.tagRepository = tagRepository;
        this.torrentRepository = torrentRepository;
        this.notificationRepository = notificationRepository;
        this.indexerRepository = indexerRepository;
        this.automationScriptRepository = automationScriptRepository;
    }

    [HttpGet]
    public ActionResult<List<TagResource>> GetAll()
    {
        var tags = this.tagRepository.All().Select(t => new TagResource
        {
            Id = t.Id,
            Label = t.Label,
        }).ToList();

        return this.Ok(tags);
    }

    [HttpGet("{id:int}")]
    public ActionResult<TagResource> Get(int id)
    {
        var tag = this.tagRepository.Get(id);
        if (tag == null)
        {
            return this.NotFound();
        }

        return this.Ok(new TagResource
        {
            Id = tag.Id,
            Label = tag.Label,
        });
    }

    [HttpPost]
    public ActionResult<TagResource> Create([FromBody] TagResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Label))
        {
            return this.BadRequest();
        }

        var trimmedLabel = resource.Label.Trim();
        if (trimmedLabel.Contains(',') || trimmedLabel.Contains('\0') || trimmedLabel.Length > 100)
        {
            return this.BadRequest("Tag label cannot be empty, contain commas or null characters, or exceed 100 characters.");
        }

        var existing = this.tagRepository.GetByLabel(trimmedLabel);
        if (existing != null)
        {
            return this.Ok(new TagResource
            {
                Id = existing.Id,
                Label = existing.Label,
            });
        }

        var model = new Tag
        {
            Label = trimmedLabel,
        };

        var inserted = this.tagRepository.Insert(model);
        return this.Ok(new TagResource
        {
            Id = inserted.Id,
            Label = inserted.Label,
        });
    }

    [HttpPut]
    [HttpPut("{id:int}")]
    public ActionResult<TagResource> Update([FromBody] TagResource resource, int id = 0)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Label))
        {
            return this.BadRequest();
        }

        var targetId = id > 0 ? id : resource.Id;
        if (targetId <= 0)
        {
            return this.BadRequest();
        }

        var trimmedLabel = resource.Label.Trim();
        if (trimmedLabel.Contains(',') || trimmedLabel.Contains('\0') || trimmedLabel.Length > 100)
        {
            return this.BadRequest("Tag label cannot be empty, contain commas or null characters, or exceed 100 characters.");
        }

        var existing = this.tagRepository.Get(targetId);
        if (existing == null)
        {
            return this.NotFound();
        }

        var duplicate = this.tagRepository.GetByLabel(trimmedLabel);
        if (duplicate != null && duplicate.Id != targetId)
        {
            return this.BadRequest($"A tag with label '{trimmedLabel}' already exists.");
        }

        existing.Label = trimmedLabel;
        this.tagRepository.Update(existing);

        return this.Ok(new TagResource
        {
            Id = existing.Id,
            Label = existing.Label,
        });
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        var tag = this.tagRepository.Get(id);
        this.tagRepository.Delete(id);

        if (this.torrentRepository != null)
        {
            var torrents = this.torrentRepository.All();
            if (torrents != null)
            {
                foreach (var torrent in torrents)
                {
                    var updated = false;
                    if (torrent.TagIds != null && torrent.TagIds.RemoveAll(t => t == id) > 0)
                    {
                        updated = true;
                    }

                    if (tag != null && !string.IsNullOrEmpty(torrent.Label))
                    {
                        var remaining = torrent.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim())
                            .Where(t => !string.Equals(t, tag.Label, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var newLabel = string.Join(", ", remaining);
                        if (newLabel != torrent.Label)
                        {
                            torrent.Label = newLabel;
                            updated = true;
                        }
                    }

                    if (updated)
                    {
                        this.torrentRepository.Update(torrent);
                    }
                }
            }
        }

        if (this.notificationRepository != null)
        {
            var notifications = this.notificationRepository.All();
            if (notifications != null)
            {
                foreach (var notification in notifications)
                {
                    if (notification.Tags != null && notification.Tags.RemoveAll(t => t == id) > 0)
                    {
                        this.notificationRepository.Update(notification);
                    }
                }
            }
        }

        if (this.indexerRepository != null)
        {
            var indexers = this.indexerRepository.All();
            if (indexers != null)
            {
                foreach (var indexer in indexers)
                {
                    if (indexer.Tags != null && indexer.Tags.RemoveAll(t => t == id) > 0)
                    {
                        this.indexerRepository.Update(indexer);
                    }
                }
            }
        }

        if (this.automationScriptRepository != null)
        {
            var scripts = this.automationScriptRepository.All();
            if (scripts != null)
            {
                foreach (var script in scripts)
                {
                    if (script.TargetTagIds != null && script.TargetTagIds.RemoveAll(t => t == id) > 0)
                    {
                        this.automationScriptRepository.Update(script);
                    }
                }
            }
        }

        return this.Ok();
    }
}
