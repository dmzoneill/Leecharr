#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Automation;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Automation;

[V1ApiController("automation")]
public class AutomationController : RestControllerWithSignalR<AutomationScriptResource, AutomationScript>
{
    private readonly IAutomationService _automationService;
    private readonly IAutomationMarketplaceService _marketplaceService;

    public AutomationController(
        IAutomationService automationService,
        IAutomationMarketplaceService marketplaceService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _automationService = automationService;
        _marketplaceService = marketplaceService;
    }

    [HttpGet]
    public ActionResult<List<AutomationScriptResource>> GetAll()
    {
        return _automationService.GetAll().Select(ToResource).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<AutomationScriptResource> Get(int id)
    {
        var script = _automationService.Get(id);
        if (script == null)
        {
            return NotFound();
        }

        return ToResource(script);
    }

    [HttpPost]
    public ActionResult<AutomationScriptResource> Create([FromBody] AutomationScriptResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Script name is required.");
        }

        var model = ToModel(resource);
        var added = _automationService.Add(model);
        return ToResource(added);
    }

    [HttpPut]
    public ActionResult<AutomationScriptResource> Update([FromBody] AutomationScriptResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Script name is required.");
        }

        var model = ToModel(resource);
        var updated = _automationService.Update(model);
        return ToResource(updated);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _automationService.Delete(id);
        return Ok();
    }

    [HttpPost("{id:int}/run")]
    public ActionResult<AutomationExecutionResult> Run(int id, [FromQuery] int? torrentId = null)
    {
        var result = _automationService.ExecuteScript(id, torrentId);
        return Ok(result);
    }

    [HttpPost("test")]
    public ActionResult<AutomationExecutionResult> Test([FromBody] AutomationTestRequestResource request)
    {
        if (request?.Script == null)
        {
            return BadRequest("Script definition is required.");
        }

        var model = ToModel(request.Script);
        var result = _automationService.TestScript(model, request.TorrentId, request.CustomInputs);
        return Ok(result);
    }

    [HttpGet("marketplace")]
    public ActionResult<List<AutomationMarketplaceTemplate>> GetMarketplaceTemplates()
    {
        return Ok(_marketplaceService.GetTemplates());
    }

    [HttpPost("marketplace/install")]
    public ActionResult<AutomationScriptResource> InstallMarketplaceTemplate([FromBody] InstallMarketplaceTemplateRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TemplateId))
        {
            return BadRequest("Template ID is required.");
        }

        try
        {
            var script = _marketplaceService.InstallTemplate(request.TemplateId, request.CustomName, request.CustomInputs);
            var added = _automationService.Add(script);
            return ToResource(added);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    protected override AutomationScriptResource GetResourceById(AutomationScript model)
    {
        return ToResource(model);
    }

    private static AutomationScriptResource ToResource(AutomationScript model)
    {
        if (model == null)
        {
            return null!;
        }

        return new AutomationScriptResource
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            Trigger = model.Trigger,
            Language = model.Language,
            Code = model.Code,
            InputsJson = model.InputsJson,
            IsEnabled = model.IsEnabled,
            TargetCategories = model.TargetCategories ?? new List<string>(),
            TargetTagIds = model.TargetTagIds ?? new List<int>(),
            CreatedAt = model.CreatedAt,
            LastExecutedAt = model.LastExecutedAt,
            LastExecutionStatus = model.LastExecutionStatus,
            LastExecutionLog = model.LastExecutionLog,
        };
    }

    private static AutomationScript ToModel(AutomationScriptResource resource)
    {
        return new AutomationScript
        {
            Id = resource.Id,
            Name = resource.Name ?? string.Empty,
            Description = resource.Description,
            Trigger = resource.Trigger,
            Language = resource.Language,
            Code = resource.Code ?? string.Empty,
            InputsJson = resource.InputsJson,
            IsEnabled = resource.IsEnabled,
            TargetCategories = resource.TargetCategories ?? new List<string>(),
            TargetTagIds = resource.TargetTagIds ?? new List<int>(),
            CreatedAt = resource.CreatedAt,
            LastExecutedAt = resource.LastExecutedAt,
            LastExecutionStatus = resource.LastExecutionStatus,
            LastExecutionLog = resource.LastExecutionLog,
        };
    }
}
