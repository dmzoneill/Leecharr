// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Notifications;

namespace Leecharr.Api.V1.Notifications;

[V1ApiController("notifications")]
[Route("api/v1/notification")]
[Authorize(Policy = "RequireAdmin")]
public class NotificationController : Controller
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
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

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Notification name is required.");
        }

        if (string.Equals(resource.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
        {
            var (scriptPath, _) = CustomScriptService.ParseSettings(resource.Settings);
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return this.BadRequest("Custom script path is required in settings.");
            }
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

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Notification name is required.");
        }

        var existing = this.notificationRepository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        if (string.Equals(resource.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
        {
            var (scriptPath, _) = CustomScriptService.ParseSettings(resource.Settings);
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return this.BadRequest("Custom script path is required in settings.");
            }
        }

        var model = ToModel(resource);
        model.Id = id;
        model.Settings = UnmaskSettings(model.Settings, existing.Settings);
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
        if (resource.Id > 0)
        {
            var existing = this.notificationRepository.Get(resource.Id);
            if (existing != null)
            {
                model.Settings = UnmaskSettings(model.Settings, existing.Settings);
            }
        }

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
        else if (string.Equals(notif.Implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                title = "Leecharr: Test",
                body = "This is a test notification from Leecharr. Your Apprise integration is working properly.",
                type = "info",
            };
        }
        else if (string.Equals(notif.Implementation, "Slack", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                text = "*Leecharr Test Notification*\nYour Slack notification webhook is working properly.",
                username = "Leecharr",
            };
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
            var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
            var success = await this.customScriptService.ExecuteScriptAsync(scriptPath, null, "Test", scriptArgs);
            return this.Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Script executed successfully." : "Script execution failed.",
            });
        }
        else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await NotificationEventHandler.SendEmailNotificationAsync(notif.Settings, "Test", null, null, payload);
                return this.Ok(new NotificationTestResult
                {
                    Success = true,
                    Message = "Email test notification sent successfully.",
                });
            }
            catch (Exception ex)
            {
                return this.Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Failed to send email test notification: {ex.Message}",
                });
            }
        }
        else
        {
            var targetUrl = NotificationEventHandler.ResolveTargetUrl(notif.Implementation, notif.Settings);
            var customHeaders = NotificationEventHandler.ResolveCustomHeaders(notif.Implementation, notif.Settings);
            var success = await this.webhookDispatcher.DispatchAsync(targetUrl, payload, customHeaders);
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
            Settings = MaskSettings(n.Settings),
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

    private static readonly HashSet<string> SensitiveSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "botToken", "bot_token", "apiKey", "api_key",
        "userKey", "user_key", "password", "pass", "secret",
        "clientSecret", "client_secret", "smtpPassword", "webhookSecret",
    };

    private static bool IsSensitiveKey(string key)
    {
        return SensitiveSettingKeys.Contains(key) ||
               key.EndsWith("password", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith("secret", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith("token", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return settings;
        }

        var trimmed = settings.TrimStart();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                var node = JsonNode.Parse(settings);
                if (node is JsonObject obj)
                {
                    MaskJsonObject(obj);
                    return obj.ToJsonString();
                }
            }
            catch (Exception ex)
            {
                Logger.Trace(ex, "Failed to parse notification settings JSON during masking");
            }
        }

        return settings;
    }

    private static void MaskJsonObject(JsonObject obj)
    {
        foreach (var property in obj.ToList())
        {
            if (IsSensitiveKey(property.Key))
            {
                if (property.Value is JsonValue val && val.TryGetValue<string>(out var strVal) && !string.IsNullOrEmpty(strVal))
                {
                    obj[property.Key] = "********";
                }
            }
            else if (property.Value is JsonObject childObj)
            {
                MaskJsonObject(childObj);
            }
        }
    }

    private static string UnmaskSettings(string newSettings, string existingSettings)
    {
        if (string.IsNullOrWhiteSpace(newSettings))
        {
            return newSettings;
        }

        if (string.IsNullOrWhiteSpace(existingSettings))
        {
            return newSettings;
        }

        var newTrimmed = newSettings.TrimStart();
        var existingTrimmed = existingSettings.TrimStart();

        if (newTrimmed.StartsWith("{") && existingTrimmed.StartsWith("{"))
        {
            try
            {
                var newNode = JsonNode.Parse(newSettings);
                var existingNode = JsonNode.Parse(existingSettings);

                if (newNode is JsonObject newObj && existingNode is JsonObject existingObj)
                {
                    UnmaskJsonObject(newObj, existingObj);
                    return newObj.ToJsonString();
                }
            }
            catch (Exception ex)
            {
                Logger.Trace(ex, "Failed to parse notification settings JSON during unmasking");
            }
        }

        return newSettings;
    }

    private static void UnmaskJsonObject(JsonObject newObj, JsonObject existingObj)
    {
        foreach (var property in newObj.ToList())
        {
            if (property.Value is JsonValue val && val.TryGetValue<string>(out var strVal) && (strVal == "********" || strVal.Contains('*')))
            {
                if (existingObj.TryGetPropertyValue(property.Key, out var existingVal) && existingVal != null)
                {
                    newObj[property.Key] = existingVal.DeepClone();
                }
            }
            else if (property.Value is JsonObject childNewObj && existingObj.TryGetPropertyValue(property.Key, out var childExisting) && childExisting is JsonObject childExistingObj)
            {
                UnmaskJsonObject(childNewObj, childExistingObj);
            }
        }
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
            catch (Exception ex)
            {
                Logger.Trace(ex, "Failed to parse JSON document while extracting notification properties");
            }
        }

        foreach (var prop in propertyNames)
        {
            if (settings.Contains(prop + "="))
            {
                var escapedProp = global::System.Text.RegularExpressions.Regex.Escape(prop);
                var match = global::System.Text.RegularExpressions.Regex.Match(settings, $@"{escapedProp}=([^&]+)");
                if (match.Success)
                {
                    return Uri.UnescapeDataString(match.Groups[1].Value);
                }
            }
        }

        return string.Empty;
    }
}
