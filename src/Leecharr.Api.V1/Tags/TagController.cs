using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Tags;

public class TagResource : RestResource
{
    public string Label { get; set; }
}

[V1ApiController("tag")]
public class TagController : Controller
{
    private static readonly ConcurrentDictionary<int, TagResource> Store = new();
    private static int _idCounter = 1;

    [HttpGet]
    public ActionResult<List<TagResource>> GetAll()
    {
        return Ok(Store.Values.ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<TagResource> Get(int id)
    {
        if (Store.TryGetValue(id, out var item))
        {
            return Ok(item);
        }

        return NotFound();
    }

    [HttpPost]
    public ActionResult<TagResource> Create([FromBody] TagResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Label))
        {
            return BadRequest();
        }

        resource.Id = _idCounter++;
        Store[resource.Id] = resource;
        return Ok(resource);
    }

    [HttpPut("{id:int}")]
    public ActionResult<TagResource> Update(int id, [FromBody] TagResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        if (!Store.ContainsKey(id))
        {
            return NotFound();
        }

        resource.Id = id;
        Store[id] = resource;
        return Ok(resource);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        Store.TryRemove(id, out _);
        return Ok();
    }
}
