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
    IHandle<ArchiveExtractionFailedEvent>,
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

    public void Handle(ArchiveExtractionFailedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.Dispatch(n => n.OnHealthIssue, "OnHealthIssue", message.Torrent);
        this.Dispatch(n => n.OnManualInteractionRequired, "OnManualInteractionRequired", message.Torrent);
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
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(scriptPath, null, "OnHealthIssue", scriptArgs).ConfigureAwait(false);
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
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(scriptPath, null, "OnApplicationUpdate", scriptArgs).ConfigureAwait(false);
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
            path = f.Path,
            size = f.Size,
            progress = f.Progress,
        }).ToList();

        var (seasonNum, epNum, epTitle) = ExtractEpisodicInfo(torrent.Name);
        var (container, resolution, videoCodec, hdrFormat, audioCodec, audioChannels, audioLanguage, subtitleLanguages) = ExtractStreamSpecs(meta?.MediaInfoJson);

        var downloadTimeSeconds = torrent.CumulativeSeedingTimeSeconds > 0
            ? torrent.CumulativeSeedingTimeSeconds
            : (torrent.DateCompleted.HasValue && torrent.DateAdded != default && torrent.DateCompleted.Value >= torrent.DateAdded
                ? (long)(torrent.DateCompleted.Value - torrent.DateAdded).TotalSeconds
                : 0L);

        var payload = new
        {
            eventType,
            instanceName = "Leecharr",
            applicationVersion = "1.0.0",
            timestamp = DateTime.UtcNow.ToString("o"),
            torrent = new
            {
                id = torrent.Id,
                name = torrent.Name,
                infoHash = torrent.InfoHash,
                category = torrent.Category,
                state = torrent.Status.ToString(),
                status = torrent.Status.ToString(),
                progress = double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0,
                totalSize = torrent.TotalSize,
                downloaded = torrent.Downloaded,
                uploaded = torrent.Uploaded,
                downloadPath = torrent.SavePath,
                savePath = torrent.SavePath,
                downloadSpeed = torrent.DownloadSpeed,
                uploadSpeed = torrent.UploadSpeed,
                eta = torrent.Eta,
                etaString = FormatEta(torrent.Eta),
                seeders = torrent.Seeders,
                leechers = torrent.Leechers,
                ratio = double.IsFinite(torrent.Ratio) ? torrent.Ratio : 0.0,
                dateAdded = torrent.DateAdded != default ? torrent.DateAdded.ToString("o") : null,
                dateCompleted = torrent.DateCompleted?.ToString("o"),
                downloadTimeSeconds,
                tags = torrent.TagIds ?? new List<int>(),
            },
            media = new
            {
                arrType = meta?.ArrType,
                arrMediaId = meta?.ArrMediaId ?? 0,
                title = meta?.Title ?? torrent.Name,
                year = meta?.Year ?? 0,
                seasonNumber = seasonNum,
                episodeNumber = epNum,
                episodeTitle = epTitle,
                overview = meta?.Overview,
                posterUrl = meta?.PosterUrl,
                fanartUrl = meta?.BackdropUrl,
                backdropUrl = meta?.BackdropUrl,
                rating = (meta?.Rating != null && double.IsFinite((double)meta.Rating)) ? (double)meta.Rating : 0.0,
                imdbId = meta?.ImdbId,
                tmdbId = meta?.TmdbId,
                tvdbId = meta?.TvdbId,
            },
            streamSpecs = new
            {
                container,
                containerFormat = container,
                resolution,
                videoCodec,
                hdrFormat,
                audioCodec,
                audioChannels,
                audioLanguage,
                subtitleLanguages,
            },
            files,
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
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(scriptPath, torrent, eventType, scriptArgs).ConfigureAwait(false);
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

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.pushover.net/1/messages.json";
        }

        var candidateUrl = trimmed;

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
                        candidateUrl = resolved.Trim();
                    }
                }
            }
            catch
            {
                // Fall back to trimmed string
            }
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(candidateUrl))
        {
            var clean = candidateUrl.TrimEnd('/');
            return clean.EndsWith("/notify", StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}/notify";
        }

        return candidateUrl;
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

    public static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (c is '_' or '*' or '[' or ']' or '(' or ')' or '~' or '>' or '|' or '\\' or '`')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
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

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Length > maxLength ? value.Substring(0, maxLength - 3) + "..." : value;
    }

    private static string ExtractOverview(dynamic meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            return (string)meta.Overview;
        }
        catch
        {
            return null;
        }
    }

    internal static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload, string settings = null)
    {
        var (chatId, token, user) = ExtractProviderSettings(settings);
        var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);

        if (string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            var title = Truncate($"[{eventType}] {torrentName}", 256);
            var torrentDetails = torrent != null
                ? $"Category: {torrent.Category ?? "None"} | Status: {torrent.Status} | Progress: {torrent.Progress * 100:F1}% | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}");
            var overview = ExtractOverview(meta);
            var rawDesc = !string.IsNullOrWhiteSpace(overview)
                ? $"{torrentDetails}\n\n{overview}"
                : torrentDetails;
            var desc = Truncate(rawDesc, 4096);

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
                        timestamp = DateTime.UtcNow.ToString("o"),
                    },
                },
            };
        }

        if (string.Equals(implementation, "Slack", StringComparison.OrdinalIgnoreCase))
        {
            var text = torrent != null
                ? $"*Leecharr [{eventType}]* - *{torrent.Name}*\nCategory: {torrent.Category ?? "None"} | Status: {torrent.Status} | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : $"*Leecharr [{eventType}]*\n{ExtractMessage(genericPayload, eventType)}";

            return new
            {
                text = Truncate(text, 3500),
                username = "Leecharr",
            };
        }

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var text = torrent != null
                ? $"*Leecharr [{EscapeMarkdown(eventType)}]*\n*{EscapeMarkdown(torrent.Name)}*\nCategory: {EscapeMarkdown(torrent.Category ?? "None")}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}"
                : $"*Leecharr [{EscapeMarkdown(eventType)}]*\n{EscapeMarkdown(ExtractMessage(genericPayload, eventType))}";

            var payloadDict = new Dictionary<string, object>
            {
                ["text"] = Truncate(text, 4096),
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

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            var title = $"Leecharr: {eventType}";
            var body = torrent != null
                ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? "None"}\nStatus: {torrent.Status}\nProgress: {torrent.Progress * 100:F1}%\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}");

            var isWarning = eventType.Contains("HealthIssue", StringComparison.OrdinalIgnoreCase) ||
                            eventType.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                            eventType.Contains("Failed", StringComparison.OrdinalIgnoreCase);

            return new
            {
                title,
                body,
                type = isWarning ? "warning" : "info",
            };
        }

        return genericPayload;
    }

    public static void SendEmailNotification(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Action<System.Net.Mail.SmtpClient, System.Net.Mail.MailMessage> smtpSender = null)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            throw new ArgumentException("Email settings are required.", nameof(settings));
        }

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
            throw new InvalidOperationException("Recipient email address ('to') is required.");
        }

        var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);
        var subject = $"[Leecharr] [{eventType}] {torrentName}";
        var torrentDetails = torrent != null
            ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? "None"}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
            : ExtractMessage(genericPayload, $"Event: {eventType}");
        var overview = ExtractOverview(meta);
        var body = !string.IsNullOrWhiteSpace(overview)
            ? $"{torrentDetails}\n\n{overview}"
            : torrentDetails;

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

        if (smtpSender != null)
        {
            smtpSender(client, mail);
        }
        else
        {
            client.Send(mail);
        }
    }

    private static string FormatEta(long seconds)
    {
        if (seconds <= 0 || seconds >= 8640000)
        {
            return "00:00:00";
        }

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 24
            ? $"{(int)ts.TotalDays}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null, null);
        }

        var match = Regex.Match(name, @"(?i)\bS(\d{1,2})E(\d{1,3})\b");
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var s) &&
            int.TryParse(match.Groups[2].Value, out var e))
        {
            return (s, e, null);
        }

        var matchAlt = Regex.Match(name, @"(?i)\b(\d{1,2})x(\d{1,3})\b");
        if (matchAlt.Success &&
            int.TryParse(matchAlt.Groups[1].Value, out var sAlt) &&
            int.TryParse(matchAlt.Groups[2].Value, out var eAlt))
        {
            return (sAlt, eAlt, null);
        }

        return (null, null, null);
    }

    private static (string ContainerFormat, string Resolution, string VideoCodec, string HdrFormat, string AudioCodec, string AudioChannels, string AudioLanguage, List<string> SubtitleLanguages) ExtractStreamSpecs(string mediaInfoJson)
    {
        if (string.IsNullOrWhiteSpace(mediaInfoJson))
        {
            return (null, null, null, null, null, null, null, new List<string>());
        }

        try
        {
            using var doc = JsonDocument.Parse(mediaInfoJson);
            var root = doc.RootElement;
            var container = root.TryGetProperty("ContainerFormat", out var c) ? c.GetString() : null;
            var resolution = root.TryGetProperty("Resolution", out var r) ? r.GetString() : null;
            var videoCodec = root.TryGetProperty("VideoCodec", out var v) ? v.GetString() : null;
            var hdr = root.TryGetProperty("HdrFormat", out var h) ? h.GetString() : null;
            var audioCodec = root.TryGetProperty("AudioCodec", out var a) ? a.GetString() : null;
            var audioChannels = root.TryGetProperty("AudioChannels", out var ac) ? ac.GetString() : null;
            var audioLanguage = root.TryGetProperty("AudioLanguage", out var al) ? al.GetString() : null;
            var subtitleLanguages = new List<string>();

            if (root.TryGetProperty("SubtitleTracks", out var st) && st.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in st.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        subtitleLanguages.Add(s);
                    }
                }
            }

            return (container, resolution, videoCodec, hdr, audioCodec, audioChannels, audioLanguage, subtitleLanguages);
        }
        catch
        {
            return (null, null, null, null, null, null, null, new List<string>());
        }
    }
}
