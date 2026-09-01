using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Api.V1.ArrIntegration;

[V1ApiController("arrconnections")]
public class ArrConnectionController : Controller
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IArrConnectionRepository _repository;

    public ArrConnectionController(IArrConnectionRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public ActionResult<List<ArrConnectionResource>> GetAll()
    {
        var definitions = _repository.All();
        return Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<ArrConnectionResource> Get(int id)
    {
        var definition = _repository.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<ArrConnectionResource> Create([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        var created = _repository.Insert(model);
        return Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ArrConnectionResource> Update(int id, [FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var existing = _repository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        _repository.Update(model);
        return Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _repository.Delete(id);
        return Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<ArrTestResult>> Test(int id)
    {
        var definition = _repository.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return await TestDirectInternal(ToResource(definition));
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

    private static ArrConnectionResource ToResource(ArrConnectionDefinition model)
    {
        return new ArrConnectionResource
        {
            Id = model.Id,
            Name = model.Name,
            ArrType = model.ArrType,
            Url = model.Url,
            ApiKey = model.ApiKey,
            Enabled = model.Enable,
            SyncCategories = model.SyncCategories,
            RefreshIntervalMinutes = model.SyncIntervalMinutes
        };
    }

    private static ArrConnectionDefinition ToModel(ArrConnectionResource resource)
    {
        return new ArrConnectionDefinition
        {
            Id = resource.Id,
            Name = resource.Name,
            ArrType = resource.ArrType ?? "Sonarr",
            Implementation = resource.ArrType ?? "Sonarr",
            Url = resource.Url,
            ApiKey = resource.ApiKey,
            Enable = resource.Enabled,
            SyncCategories = resource.SyncCategories,
            SyncIntervalMinutes = resource.RefreshIntervalMinutes > 0 ? resource.RefreshIntervalMinutes : 15,
            SyncEnabled = resource.Enabled
        };
    }

    private async Task<ActionResult<ArrTestResult>> TestDirectInternal(ArrConnectionResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return Ok(new ArrTestResult { Success = false, Message = "URL is required." });
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, resource.Url.TrimEnd('/') + "/api/v3/system/status");
            if (!string.IsNullOrWhiteSpace(resource.ApiKey))
            {
                req.Headers.Add("X-Api-Key", resource.ApiKey);
            }

            var resp = await HttpClient.SendAsync(req);
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
