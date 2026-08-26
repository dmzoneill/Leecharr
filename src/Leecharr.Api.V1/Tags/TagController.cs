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
    private readonly ITagRepository _tagRepository;

    public TagController(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    [HttpGet]
    public ActionResult<List<TagResource>> GetAll()
    {
        var tags = _tagRepository.All().Select(t => new TagResource
        {
            Id = t.Id,
            Label = t.Label
        }).ToList();

        return Ok(tags);
    }

    [HttpGet("{id:int}")]
    public ActionResult<TagResource> Get(int id)
    {
        var tag = _tagRepository.Get(id);
        if (tag == null)
        {
            return NotFound();
        }

        return Ok(new TagResource
        {
            Id = tag.Id,
            Label = tag.Label
        });
    }

    [HttpPost]
    public ActionResult<TagResource> Create([FromBody] TagResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Label))
        {
            return BadRequest();
        }

        var existing = _tagRepository.GetByLabel(resource.Label.Trim());
        if (existing != null)
        {
            return Ok(new TagResource
            {
                Id = existing.Id,
                Label = existing.Label
            });
        }

        var model = new Tag
        {
            Label = resource.Label.Trim()
        };

        var inserted = _tagRepository.Insert(model);
        return Ok(new TagResource
        {
            Id = inserted.Id,
            Label = inserted.Label
        });
    }

    [HttpPut("{id:int}")]
    public ActionResult<TagResource> Update(int id, [FromBody] TagResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Label))
        {
            return BadRequest();
        }

        var existing = _tagRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        existing.Label = resource.Label.Trim();
        _tagRepository.Update(existing);

        return Ok(new TagResource
        {
            Id = existing.Id,
            Label = existing.Label
        });
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _tagRepository.Delete(id);
        return Ok();
    }
}
