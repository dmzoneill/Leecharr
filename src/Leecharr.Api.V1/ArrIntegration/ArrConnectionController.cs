using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;

namespace Leecharr.Api.V1.ArrIntegration;

[V1ApiController("arrconnections")]
public class ArrConnectionController : Controller
{
    private static readonly ConcurrentDictionary<int, ArrConnectionResource> Store = new();
    private static int _idCounter = 1;

    [HttpGet]
    public ActionResult<List<ArrConnectionResource>> GetAll()
    {
        return Ok(Store.Values.ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<ArrConnectionResource> Get(int id)
    {
        if (Store.TryGetValue(id, out var item))
        {
            return Ok(item);
        }

        return NotFound();
    }

    [HttpPost]
    public ActionResult<ArrConnectionResource> Create([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        resource.Id = _idCounter++;
        Store[resource.Id] = resource;
        return Ok(resource);
    }

    [HttpPut("{id:int}")]
    public ActionResult<ArrConnectionResource> Update(int id, [FromBody] ArrConnectionResource resource)
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

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<ArrTestResult>> Test(int id)
    {
        if (Store.TryGetValue(id, out var item))
        {
            return await TestDirectInternal(item);
        }

        return NotFound();
    }

    [HttpPost("test")]
    public async Task<ActionResult<ArrTestResult>> TestDirect([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        return await TestDirectInternal(resource);
    }

    private async Task<ActionResult<ArrTestResult>> TestDirectInternal(ArrConnectionResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return Ok(new ArrTestResult { Success = false, Message = "URL is required." });
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrWhiteSpace(resource.ApiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", resource.ApiKey);
            }

            var uri = resource.Url.TrimEnd('/') + "/api/v3/system/status";
            var resp = await client.GetAsync(uri);
            if (resp.IsSuccessStatusCode)
            {
                return Ok(new ArrTestResult { Success = true, Message = $"Connected to {resource.ArrType ?? "Arr"} successfully.", Version = "v3" });
            }

            return Ok(new ArrTestResult { Success = false, Message = $"Server returned {resp.StatusCode}" });
        }
        catch (Exception ex)
        {
            return Ok(new ArrTestResult { Success = false, Message = ex.Message });
        }
    }
}
