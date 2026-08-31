// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;

namespace Leecharr.Api.V1.Notifications;

[V1ApiController("notifications")]
public class NotificationController : Controller
{
    private readonly INotificationRepository notificationRepository;
    private readonly IWebhookDispatcher webhookDispatcher;
    private readonly ICustomScriptService customScriptService;

    public NotificationController(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService)
    {
        this.notificationRepository = notificationRepository;
        this.webhookDispatcher = webhookDispatcher;
        this.customScriptService = customScriptService;
    }

    [HttpGet]
    public ActionResult<List<NotificationResource>> GetAll()
    {
        return this.Ok(this.notificationRepository.All().Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<NotificationResource> GetById(int id)
    {
        var item = this.notificationRepository.Get(id);
        if (item == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(item));
    }

    [HttpPost]
    public ActionResult<NotificationResource> Create([FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.notificationRepository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<NotificationResource> Update(int id, [FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.notificationRepository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        this.notificationRepository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.notificationRepository.Delete(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<NotificationTestResult>> Test(int id)
    {
        var item = this.notificationRepository.Get(id);
        if (item == null)
        {
            return this.NotFound();
        }

        return await this.TestInternal(item);
    }

    [HttpPost("test")]
    public async Task<ActionResult<NotificationTestResult>> TestDirect([FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        return await this.TestInternal(model);
    }

    private async Task<ActionResult<NotificationTestResult>> TestInternal(NotificationDefinition notif)
    {
        object payload;
        if (string.Equals(notif.Implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                username = "Leecharr",
                embeds = new object[]
                {
                    new
                    {
                        title = "[Test] Leecharr Notification Test",
                        description = "This is a test notification from Leecharr. Your webhook configuration is working properly.",
                        color = 16765286, // Gold
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                },
            };
        }
        else if (string.Equals(notif.Implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var chatId = ExtractSetting(notif.Settings, "chat_id", "chatId");
            var telegramPayload = new Dictionary<string, object>
            {
                ["text"] = "*Leecharr Test Notification*\nYour Telegram notification connection is working properly.",
                ["parse_mode"] = "Markdown",
            };

            if (!string.IsNullOrEmpty(chatId))
            {
                telegramPayload["chat_id"] = chatId;
            }

            payload = telegramPayload;
        }
        else if (string.Equals(notif.Implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                title = "Leecharr: Test",
                message = "This is a test notification from Leecharr.",
                priority = 5,
            };
        }
        else if (string.Equals(notif.Implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            var token = ExtractSetting(notif.Settings, "token", "botToken", "apiKey");
            var user = ExtractSetting(notif.Settings, "user", "userKey");
            var pushoverPayload = new Dictionary<string, object>
            {
                ["title"] = "Leecharr: Test",
                ["message"] = "This is a test notification from Leecharr.",
            };

            if (!string.IsNullOrEmpty(token))
            {
                pushoverPayload["token"] = token;
            }

            if (!string.IsNullOrEmpty(user))
            {
                pushoverPayload["user"] = user;
            }

            payload = pushoverPayload;
        }
        else
        {
            payload = new
            {
                EventType = "Test",
                Message = "Leecharr test notification",
                Timestamp = DateTime.UtcNow,
            };
        }

        if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
        {
            var success = await this.customScriptService.ExecuteScriptAsync(notif.Settings, null, "Test");
            return this.Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Script executed successfully." : "Script execution failed.",
            });
        }
        else
        {
            var success = await this.webhookDispatcher.DispatchAsync(notif.Settings, payload);
            return this.Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Webhook dispatched successfully." : "Webhook dispatch failed.",
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
            Tags = n.Tags ?? new List<int>(),
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
            Tags = r.Tags ?? new List<int>(),
        };
    }

    private static string ExtractSetting(string settings, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return string.Empty;
        }

        if (settings.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = global::System.Text.Json.JsonDocument.Parse(settings);
                var root = doc.RootElement;
                foreach (var prop in propertyNames)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        return val.GetString() ?? val.ToString();
                    }
                }
            }
            catch
            {
            }
        }

        foreach (var prop in propertyNames)
        {
            if (settings.Contains(prop + "="))
            {
                var match = global::System.Text.RegularExpressions.Regex.Match(settings, $@"{prop}=([^&]+)");
                if (match.Success)
                {
                    return Uri.UnescapeDataString(match.Groups[1].Value);
                }
            }
        }

        return string.Empty;
    }
}
