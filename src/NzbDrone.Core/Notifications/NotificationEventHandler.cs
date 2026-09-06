// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public class NotificationEventHandler :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<MediaEnrichedEvent>,
    IHandle<ArchiveExtractionCompletedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<ApplicationUpdatedEvent>,
    IHandle<HealthIssueEvent>,
    IHandle<TorrentSeedGoalReachedEvent>
{
    private readonly INotificationRepository notificationRepository;
    private readonly IWebhookDispatcher webhookDispatcher;
    private readonly ICustomScriptService customScriptService;
    private readonly IConfigService configService;
    private readonly IMediaEnrichmentService mediaEnrichmentService;
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentFileRepository torrentFileRepository;
    private readonly IDownloadEngine downloadEngine;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public NotificationEventHandler(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService,
        IConfigService configService,
        IMediaEnrichmentService mediaEnrichmentService = null,
        ITorrentRepository torrentRepository = null,
        ITorrentFileRepository torrentFileRepository = null,
        IDownloadEngine downloadEngine = null)
    {
        this.notificationRepository = notificationRepository;
        this.webhookDispatcher = webhookDispatcher;
        this.customScriptService = customScriptService;
        this.configService = configService;
        this.mediaEnrichmentService = mediaEnrichmentService;
        this.torrentRepository = torrentRepository;
        this.torrentFileRepository = torrentFileRepository;
        this.downloadEngine = downloadEngine;
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.Dispatch(n => n.OnGrab, "OnGrab", message.Torrent);

        if (!string.IsNullOrWhiteSpace(this.configService?.ScriptTorrentAddedFilename))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.customScriptService.ExecuteScriptAsync(this.configService.ScriptTorrentAddedFilename, message.Torrent, "OnGrab").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error executing ScriptTorrentAdded script");
                }
            });
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.Dispatch(n => n.OnDownloadComplete, "OnDownloadComplete", message.Torrent);

        if (!string.IsNullOrWhiteSpace(this.configService?.OnDownloadCompleteScript))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.customScriptService.ExecuteScriptAsync(this.configService.OnDownloadCompleteScript, message.Torrent, "OnDownloadComplete").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error executing OnDownloadComplete script");
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(this.configService?.ScriptTorrentDoneFilename))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.customScriptService.ExecuteScriptAsync(this.configService.ScriptTorrentDoneFilename, message.Torrent, "OnDownloadComplete").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error executing ScriptTorrentDone script");
                }
            });
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.Dispatch(n => n.OnTorrentDeleted, "OnTorrentDeleted", message.Torrent);
    }

    public void Handle(MediaEnrichedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrent = this.torrentRepository?.Get(message.TorrentId);
        if (torrent != null)
        {
            this.Dispatch(n => n.OnMediaInspected, "OnMediaInspected", torrent);
        }
    }

    public void Handle(ArchiveExtractionCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.Dispatch(n => n.OnExtractComplete, "OnExtractComplete", message.Torrent);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus is TorrentStatus.Error or TorrentStatus.Stalled)
        {
            this.Dispatch(n => n.OnHealthIssue, "OnHealthIssue", message.Torrent);
            this.Dispatch(n => n.OnManualInteractionRequired, "OnManualInteractionRequired", message.Torrent);
        }
        else if ((message.OldStatus is TorrentStatus.Error or TorrentStatus.Stalled) &&
                 message.NewStatus != TorrentStatus.Error && message.NewStatus != TorrentStatus.Stalled)
        {
            this.Dispatch(n => n.OnHealthRestored, "OnHealthRestored", message.Torrent);
        }
    }

    public void Handle(TorrentSeedGoalReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.Dispatch(n => n.OnSeedGoalReached, "OnSeedGoalReached", message.Torrent);

        if (!string.IsNullOrWhiteSpace(this.configService?.OnSeedGoalReachedScript))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.customScriptService.ExecuteScriptAsync(this.configService.OnSeedGoalReachedScript, message.Torrent, "OnSeedGoalReached").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error executing OnSeedGoalReached script");
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(this.configService?.ScriptTorrentDoneSeedingFilename))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.customScriptService.ExecuteScriptAsync(this.configService.ScriptTorrentDoneSeedingFilename, message.Torrent, "OnSeedGoalReached").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error executing ScriptTorrentDoneSeeding script");
                }
            });
        }
    }

    public void Handle(HealthIssueEvent message)
    {
        var torrent = message.Torrent ?? (message.TorrentId > 0 ? this.torrentRepository?.Get(message.TorrentId) : null);
        if (torrent == null)
        {
            return;
        }

        if (message.IsResolved)
        {
            this.Dispatch(n => n.OnHealthRestored, "OnHealthRestored", torrent);
        }
        else
        {
            this.Dispatch(n => n.OnHealthIssue, "OnHealthIssue", torrent);
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        if (this.downloadEngine != null)
        {
            this.logger.Warn("Halting download engine due to VPN Kill Switch event on interface: {0}", message.InterfaceName);
            Task.Run(async () =>
            {
                try
                {
                    await this.downloadEngine.StopAsync();
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error halting download engine after VPN kill switch trigger");
                }
            });
        }

        var activeNotifications = this.notificationRepository.GetEnabled().Where(n => n.OnHealthIssue).ToList();
        var payload = new
        {
            EventType = "OnHealthIssue",
            Message = "VPN Kill Switch triggered: VPN interface disconnected. BitTorrent traffic halted.",
            Timestamp = DateTime.UtcNow,
        };

        foreach (var notif in activeNotifications)
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(notif.Settings, null, "OnHealthIssue").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error executing custom script for OnHealthIssue");
                    }
                });
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() =>
                {
                    try
                    {
                        SendEmailNotification(notif.Settings, "OnHealthIssue", null, null, payload);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error sending email notification for OnHealthIssue");
                    }
                });
            }
            else
            {
                var providerPayload = BuildProviderPayload(notif.Implementation, "OnHealthIssue", null, null, payload, notif.Settings);
                var targetUrl = ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = ResolveCustomHeaders(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error dispatching webhook notification for OnHealthIssue");
                    }
                });
            }
        }
    }

    public void Handle(ApplicationUpdatedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var activeNotifications = this.notificationRepository.GetEnabled().Where(n => n.OnApplicationUpdate).ToList();
        var payload = new
        {
            EventType = "OnApplicationUpdate",
            PreviousVersion = message.PreviousVersion ?? string.Empty,
            NewVersion = message.NewVersion ?? string.Empty,
            Message = $"Leecharr updated to version {message.NewVersion}",
            Timestamp = DateTime.UtcNow,
        };

        foreach (var notif in activeNotifications)
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(notif.Settings, null, "OnApplicationUpdate").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error executing custom script for OnApplicationUpdate");
                    }
                });
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() =>
                {
                    try
                    {
                        SendEmailNotification(notif.Settings, "OnApplicationUpdate", null, null, payload);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error sending email notification for OnApplicationUpdate");
                    }
                });
            }
            else
            {
                var providerPayload = BuildProviderPayload(notif.Implementation, "OnApplicationUpdate", null, null, payload, notif.Settings);
                var targetUrl = ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = ResolveCustomHeaders(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error dispatching notification for OnApplicationUpdate");
                    }
                });
            }
        }
    }

    private void Dispatch(Func<NotificationDefinition, bool> predicate, string eventType, Torrent torrent)
    {
        var activeNotifications = this.notificationRepository.GetEnabled().Where(predicate).ToList();
        if (activeNotifications.Count == 0)
        {
            return;
        }

        var meta = this.mediaEnrichmentService?.GetMetadata(torrent.Id);
        var files = this.torrentFileRepository?.GetByTorrentId(torrent.Id)?.Select(f => new
        {
            Path = f.Path,
            Size = f.Size,
            Progress = f.Progress,
        }).ToList();

        var payload = new
        {
            EventType = eventType,
            TorrentId = torrent.Id,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            Category = torrent.Category,
            SavePath = torrent.SavePath,
            TotalSize = torrent.TotalSize,
            Downloaded = torrent.Downloaded,
            Uploaded = torrent.Uploaded,
            DownloadSpeed = torrent.DownloadSpeed,
            UploadSpeed = torrent.UploadSpeed,
            Progress = torrent.Progress,
            Ratio = torrent.Ratio,
            Status = torrent.Status.ToString(),
            MediaTitle = meta?.Title,
            MediaYear = meta?.Year,
            MediaOverview = meta?.Overview,
            MediaGenres = meta?.Genres,
            Files = files,
            Timestamp = DateTime.UtcNow,
        };

        foreach (var notif in activeNotifications)
        {
            // Tag filtering
            if (notif.Tags != null && notif.Tags.Count > 0)
            {
                if (torrent.TagIds == null || !notif.Tags.Any(t => torrent.TagIds.Contains(t)))
                {
                    continue;
                }
            }

            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(notif.Settings, torrent, eventType).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error executing custom script for {0}", eventType);
                    }
                });
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() =>
                {
                    try
                    {
                        SendEmailNotification(notif.Settings, eventType, torrent, meta, payload);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error sending email notification for {0}", eventType);
                    }
                });
            }
            else
            {
                var providerPayload = BuildProviderPayload(notif.Implementation, eventType, torrent, meta, payload, notif.Settings);
                var targetUrl = ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = ResolveCustomHeaders(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error dispatching webhook notification for {0}", eventType);
                    }
                });
            }
        }
    }

    private static (string ChatId, string Token, string User) ExtractProviderSettings(string settings)
    {
        var chatId = string.Empty;
        var token = string.Empty;
        var user = string.Empty;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (chatId, token, user);
        }

        if (settings.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(settings);
                var root = doc.RootElement;
                if (root.TryGetProperty("chat_id", out var c) || root.TryGetProperty("chatId", out c))
                {
                    chatId = c.GetString() ?? c.ToString();
                }

                if (root.TryGetProperty("token", out var t) || root.TryGetProperty("botToken", out t) || root.TryGetProperty("apiKey", out t))
                {
                    token = t.GetString() ?? t.ToString();
                }

                if (root.TryGetProperty("user", out var u) || root.TryGetProperty("userKey", out u))
                {
                    user = u.GetString() ?? u.ToString();
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(chatId) && settings.Contains("chat_id="))
        {
            var match = System.Text.RegularExpressions.Regex.Match(settings, @"chat_id=([^&]+)");
            if (match.Success)
            {
                chatId = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(token) && settings.Contains("token="))
        {
            var match = System.Text.RegularExpressions.Regex.Match(settings, @"token=([^&]+)");
            if (match.Success)
            {
                token = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(user) && settings.Contains("user="))
        {
            var match = System.Text.RegularExpressions.Regex.Match(settings, @"user=([^&]+)");
            if (match.Success)
            {
                user = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return (chatId, token, user);
    }

    public static string ResolveTargetUrl(string implementation, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return string.Empty;
        }

        var trimmed = settings.Trim();

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var (_, token, _) = ExtractProviderSettings(trimmed);
            return string.IsNullOrWhiteSpace(token)
                ? "https://api.telegram.org/bot/sendMessage"
                : $"https://api.telegram.org/bot{token}/sendMessage";
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            var title = $"Leecharr: {eventType}";
            var body = torrent != null
                ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? "None"}\nStatus: {torrent.Status}\nProgress: {torrent.Progress * 100:F1}%"
                : ExtractMessage(genericPayload, eventType);

            return new
            {
                title,
                body,
                type = eventType.Contains("HealthIssue", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
            };
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.pushover.net/1/messages.json";
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = trimmed;
            if (trimmed.StartsWith("{"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("url", out var u) ||
                        root.TryGetProperty("webhookUrl", out u) ||
                        root.TryGetProperty("targetUrl", out u))
                    {
                        var uStr = u.GetString();
                        if (!string.IsNullOrWhiteSpace(uStr))
                        {
                            resolved = uStr.Trim();
                        }
                    }
                }
                catch
                {
                }
            }

            var cleanUrl = resolved.TrimEnd('/');
            if (!cleanUrl.EndsWith("/notify", StringComparison.OrdinalIgnoreCase))
            {
                return $"{cleanUrl}/notify";
            }

            return cleanUrl;
        }

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("url", out var u) ||
                    root.TryGetProperty("webhookUrl", out u) ||
                    root.TryGetProperty("targetUrl", out u))
                {
                    var resolved = u.GetString();
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved.Trim();
                    }
                }
            }
            catch
            {
                // Fall back to trimmed string
            }
        }

        return trimmed;
    }

    public static string ResolveCustomHeaders(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return null;
        }

        var trimmed = settings.Trim();

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var propertyNames = new[] { "headers", "Headers", "customHeaders", "CustomHeaders", "custom_headers" };
                foreach (var propName in propertyNames)
                {
                    if (root.TryGetProperty(propName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Object)
                        {
                            var raw = prop.GetRawText()?.Trim();
                            return string.IsNullOrWhiteSpace(raw) || raw == "{}" ? null : raw;
                        }

                        if (prop.ValueKind == JsonValueKind.String)
                        {
                            var str = prop.GetString()?.Trim();
                            return string.IsNullOrWhiteSpace(str) || str == "{}" ? null : str;
                        }

                        if (prop.ValueKind == JsonValueKind.Array)
                        {
                            var raw = prop.GetRawText()?.Trim();
                            return string.IsNullOrWhiteSpace(raw) || raw == "[]" ? null : raw;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to regex parsing
            }
        }

        var matchKeys = new[] { "headers=", "customHeaders=", "custom_headers=" };
        foreach (var key in matchKeys)
        {
            if (settings.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(settings, $@"{key}([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var val = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
                    return string.IsNullOrWhiteSpace(val) || val == "{}" ? null : val;
                }
            }
        }

        return null;
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("]", "\\]").Replace("`", "\\`");
    }

    private static string ExtractMessage(object payload, string fallback)
    {
        if (payload == null)
        {
            return fallback;
        }

        if (payload is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        if (payload is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("Message", out var m1) && m1 != null)
            {
                var str1 = m1.ToString();
                if (!string.IsNullOrWhiteSpace(str1))
                {
                    return str1;
                }
            }

            if (dict.TryGetValue("message", out var m2) && m2 != null)
            {
                var str2 = m2.ToString();
                if (!string.IsNullOrWhiteSpace(str2))
                {
                    return str2;
                }
            }
        }

        var prop = payload.GetType().GetProperty("Message", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var value = prop?.GetValue(payload)?.ToString();

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload, string settings = null)
    {
        var (chatId, token, user) = ExtractProviderSettings(settings);
        var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);

        if (string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            var title = $"[{eventType}] {torrentName}";
            var desc = meta?.Overview ?? (torrent != null
                ? $"Category: {torrent.Category ?? "None"} | Progress: {torrent.Progress * 100:F1}% | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}"));

            return new
            {
                username = "Leecharr",
                embeds = new object[]
                {
                    new
                    {
                        title,
                        description = desc,
                        color = 16765286, // Gold
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                },
            };
        }

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var text = torrent != null
                ? $"*Leecharr [{EscapeMarkdown(eventType)}]*\n*{EscapeMarkdown(torrent.Name)}*\nCategory: {EscapeMarkdown(torrent.Category ?? "None")}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}"
                : $"*Leecharr [{EscapeMarkdown(eventType)}]*\n{EscapeMarkdown(ExtractMessage(genericPayload, eventType))}";

            var payloadDict = new Dictionary<string, object>
            {
                ["text"] = text,
                ["parse_mode"] = "Markdown",
            };

            if (!string.IsNullOrEmpty(chatId))
            {
                payloadDict["chat_id"] = chatId;
            }

            return payloadDict;
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                title = $"Leecharr: {eventType}",
                message = torrent != null ? $"{torrent.Name} ({torrent.Category ?? "Default"}) - {torrent.Status}" : ExtractMessage(genericPayload, eventType),
                priority = 5,
            };
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            var payloadDict = new Dictionary<string, object>
            {
                ["title"] = $"Leecharr: {eventType}",
                ["message"] = torrent != null ? $"{torrent.Name} ({torrent.Category ?? "Default"}) - {torrent.Status}" : ExtractMessage(genericPayload, eventType),
            };

            if (!string.IsNullOrEmpty(token))
            {
                payloadDict["token"] = token;
            }

            if (!string.IsNullOrEmpty(user))
            {
                payloadDict["user"] = user;
            }

            return payloadDict;
        }

        return genericPayload;
    }

    public static void SendEmailNotification(string settings, string eventType, Torrent torrent, dynamic meta, object genericPayload)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return;
        }

        try
        {
            var host = "localhost";
            var port = 25;
            var ssl = false;
            string user = null;
            string pass = null;
            var from = "leecharr@localhost";
            string to = null;

            if (settings.TrimStart().StartsWith("{"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(settings);
                var root = doc.RootElement;
                if (root.TryGetProperty("server", out var s) || root.TryGetProperty("host", out s))
                {
                    host = s.GetString() ?? host;
                }

                if (root.TryGetProperty("port", out var p))
                {
                    if (p.TryGetInt32(out var pInt))
                    {
                        port = pInt;
                    }
                    else if (int.TryParse(p.GetString(), out var pParsed))
                    {
                        port = pParsed;
                    }
                }

                if (root.TryGetProperty("useSsl", out var sslProp) || root.TryGetProperty("ssl", out sslProp))
                {
                    ssl = sslProp.GetBoolean();
                }

                if (root.TryGetProperty("username", out var u) || root.TryGetProperty("user", out u))
                {
                    user = u.GetString();
                }

                if (root.TryGetProperty("password", out var pwd) || root.TryGetProperty("pass", out pwd))
                {
                    pass = pwd.GetString();
                }

                if (root.TryGetProperty("from", out var f))
                {
                    from = f.GetString() ?? from;
                }

                if (root.TryGetProperty("to", out var tProp) || root.TryGetProperty("recipient", out tProp))
                {
                    to = tProp.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(to))
            {
                return;
            }

            var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);
            var subject = $"[Leecharr] [{eventType}] {torrentName}";
            var body = meta?.Overview ?? (torrent != null
                ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? "None"}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}"));

            using var mail = new System.Net.Mail.MailMessage(from, to, subject, body);
            using var client = new System.Net.Mail.SmtpClient(host, port)
            {
                EnableSsl = ssl,
                Timeout = 10000,
            };

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                client.Credentials = new System.Net.NetworkCredential(user, pass);
            }

            client.Send(mail);
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Warn(ex, "Failed to send email notification for event {0}", eventType);
        }
    }
}
