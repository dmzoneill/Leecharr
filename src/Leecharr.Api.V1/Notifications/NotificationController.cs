using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using NzbDrone.Core.Notifications;

namespace Leecharr.Api.V1.Notifications;

[V1ApiController("notifications")]
public class NotificationController : Controller
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;

    public NotificationController(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
    }

    [HttpGet]
    public ActionResult<List<NotificationResource>> GetAll()
    {
        return Ok(_notificationRepository.All().Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<NotificationResource> GetById(int id)
    {
        var item = _notificationRepository.Get(id);
        if (item == null)
        {
            return NotFound();
        }

        return Ok(ToResource(item));
    }

    [HttpPost]
    public ActionResult<NotificationResource> Create([FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        var created = _notificationRepository.Insert(model);
        return Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<NotificationResource> Update(int id, [FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var existing = _notificationRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        _notificationRepository.Update(model);
        return Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _notificationRepository.Delete(id);
        return Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<NotificationTestResult>> Test(int id)
    {
        var item = _notificationRepository.Get(id);
        if (item == null)
        {
            return NotFound();
        }

        return await TestInternal(item);
    }

    [HttpPost("test")]
    public async Task<ActionResult<NotificationTestResult>> TestDirect([FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        return await TestInternal(model);
    }

    private async Task<ActionResult<NotificationTestResult>> TestInternal(NotificationDefinition notif)
    {
        var payload = new
        {
            EventType = "Test",
            Message = "Leecharr test notification",
            Timestamp = DateTime.UtcNow
        };

        if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
        {
            var success = await _customScriptService.ExecuteScriptAsync(notif.Settings, null, "Test");
            return Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Script executed successfully." : "Script execution failed."
            });
        }
        else
        {
            var success = await _webhookDispatcher.DispatchAsync(notif.Settings, payload);
            return Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Webhook dispatched successfully." : "Webhook dispatch failed."
            });
        }
    }

    private static NotificationResource ToResource(NotificationDefinition n)
    {
        return new NotificationResource
        {
            Id = n.Id,
            Name = n.Name,
            Implementation = n.Implementation,
            ConfigContract = n.ConfigContract,
            Settings = n.Settings,
            Enable = n.Enable,
            OnGrab = n.OnGrab,
            OnDownloadComplete = n.OnDownloadComplete,
            OnMediaInspected = n.OnMediaInspected,
            OnExtractComplete = n.OnExtractComplete,
            OnSeedGoalReached = n.OnSeedGoalReached,
            OnTorrentDeleted = n.OnTorrentDeleted,
            OnHealthIssue = n.OnHealthIssue,
            OnHealthRestored = n.OnHealthRestored,
            OnManualInteractionRequired = n.OnManualInteractionRequired,
            OnApplicationUpdate = n.OnApplicationUpdate,
            Tags = n.Tags ?? new List<int>()
        };
    }

    private static NotificationDefinition ToModel(NotificationResource r)
    {
        return new NotificationDefinition
        {
            Id = r.Id,
            Name = r.Name,
            Implementation = r.Implementation ?? "Webhook",
            ConfigContract = r.ConfigContract,
            Settings = r.Settings,
            Enable = r.Enable,
            OnGrab = r.OnGrab,
            OnDownloadComplete = r.OnDownloadComplete,
            OnMediaInspected = r.OnMediaInspected,
            OnExtractComplete = r.OnExtractComplete,
            OnSeedGoalReached = r.OnSeedGoalReached,
            OnTorrentDeleted = r.OnTorrentDeleted,
            OnHealthIssue = r.OnHealthIssue,
            OnHealthRestored = r.OnHealthRestored,
            OnManualInteractionRequired = r.OnManualInteractionRequired,
            OnApplicationUpdate = r.OnApplicationUpdate,
            Tags = r.Tags ?? new List<int>()
        };
    }
}
