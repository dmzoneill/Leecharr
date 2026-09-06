// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Api.V1.ArrIntegration;

[V1ApiController("arrconnections")]
[Authorize(Policy = "RequireOperator")]
public class ArrConnectionController : Controller
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IArrConnectionRepository repository;

    public ArrConnectionController(IArrConnectionRepository repository)
    {
        this.repository = repository;
    }

    [HttpGet]
    public ActionResult<List<ArrConnectionResource>> GetAll()
    {
        var definitions = this.repository.All();
        return this.Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<ArrConnectionResource> Get(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<ArrConnectionResource> Create([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.repository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ArrConnectionResource> Update(int id, [FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.repository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        if (model.ApiKey == "********" || (model.ApiKey != null && model.ApiKey.Contains('*')))
        {
            model.ApiKey = existing.ApiKey;
        }

        this.repository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.repository.Delete(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<ArrTestResult>> Test(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return await this.TestDirectInternal(ToResource(definition));
    }

    [HttpPost("test")]
    public async Task<ActionResult<ArrTestResult>> TestDirect([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        if (resource.Id > 0 && (resource.ApiKey == "********" || (resource.ApiKey != null && resource.ApiKey.Contains('*'))))
        {
            var existing = this.repository.Get(resource.Id);
            if (existing != null)
            {
                resource.ApiKey = existing.ApiKey;
            }
        }

        return await this.TestDirectInternal(resource);
    }

    private static ArrConnectionResource ToResource(ArrConnectionDefinition model)
    {
        return new ArrConnectionResource
        {
            Id = model.Id,
            Name = model.Name,
            ArrType = model.ArrType,
            Url = model.Url,
            ApiKey = string.IsNullOrEmpty(model.ApiKey) ? string.Empty : "********",
            Enabled = model.Enable,
            SyncCategories = model.SyncCategories,
            RefreshIntervalMinutes = model.SyncIntervalMinutes,
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
            SyncEnabled = resource.Enabled,
        };
    }

    private async Task<ActionResult<ArrTestResult>> TestDirectInternal(ArrConnectionResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return this.Ok(new ArrTestResult { Success = false, Message = "URL is required." });
        }

        var baseUrl = resource.Url.TrimEnd('/');
        var endpoints = new[] { "/api/v3/system/status", "/api/v1/system/status" };
        string lastError = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + endpoint);
                if (!string.IsNullOrWhiteSpace(resource.ApiKey))
                {
                    req.Headers.Add("X-Api-Key", resource.ApiKey);
                }

                var resp = await HttpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var version = endpoint.Contains("v3") ? "v3" : "v1";
                    return this.Ok(new ArrTestResult
                    {
                        Success = true,
                        Message = $"Connected to {resource.ArrType ?? "Arr"} successfully.",
                        Version = version,
                    });
                }

                lastError = $"Server returned HTTP {(int)resp.StatusCode} {resp.StatusCode}.";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return this.Ok(new ArrTestResult
        {
            Success = false,
            Message = lastError ?? $"Failed to connect to {resource.ArrType ?? "Arr"}.",
        });
    }
}
