// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tags;

namespace Leecharr.Api.V1.Tags;

public class TagResource : RestResource
{
    public string Label { get; set; }
}

[V1ApiController("tag")]
public class TagController : Controller
{
    private readonly ITagRepository tagRepository;

    public TagController(ITagRepository tagRepository)
    {
        this.tagRepository = tagRepository;
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

        var existing = this.tagRepository.GetByLabel(resource.Label.Trim());
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
            Label = resource.Label.Trim(),
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

        var existing = this.tagRepository.Get(targetId);
        if (existing == null)
        {
            return this.NotFound();
        }

        existing.Label = resource.Label.Trim();
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
        this.tagRepository.Delete(id);
        return this.Ok();
    }
}
