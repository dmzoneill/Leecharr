// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IEpisodicParser episodicParser;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public NotificationEventHandler(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService,
        IConfigService configService,
        IMediaEnrichmentService mediaEnrichmentService = null,
        ITorrentRepository torrentRepository = null,
        ITorrentFileRepository torrentFileRepository = null,
        IDownloadEngine downloadEngine = null,
        IEpisodicParser episodicParser = null)
    {
        this.notificationRepository = notificationRepository;
        this.webhookDispatcher = webhookDispatcher;
        this.customScriptService = customScriptService;
        this.configService = configService;
        this.mediaEnrichmentService = mediaEnrichmentService;
        this.torrentRepository = torrentRepository;
        this.torrentFileRepository = torrentFileRepository;
        this.downloadEngine = downloadEngine;
        this.episodicParser = episodicParser ?? new EpisodicParser();
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
        if (message == null)
        {
            return;
        }

        var torrent = message.Torrent ?? (message.TorrentId > 0 ? this.torrentRepository?.Get(message.TorrentId) : null);
        if (torrent != null)
        {
            if (message.IsResolved)
            {
                this.Dispatch(n => n.OnHealthRestored, "OnHealthRestored", torrent);
            }
            else
            {
                this.Dispatch(n => n.OnHealthIssue, "OnHealthIssue", torrent);
            }

            return;
        }

        var eventType = message.IsResolved ? "OnHealthRestored" : "OnHealthIssue";
        var payload = new
        {
            EventType = eventType,
            Source = message.Source,
            Message = message.Message,
            IsResolved = message.IsResolved,
            Timestamp = DateTime.UtcNow,
        };

        this.DispatchGeneric(n => message.IsResolved ? n.OnHealthRestored : n.OnHealthIssue, eventType, payload);
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

        var payload = new
        {
            EventType = "OnHealthIssue",
            Message = "VPN Kill Switch triggered: VPN interface disconnected. BitTorrent traffic halted.",
            Timestamp = DateTime.UtcNow,
        };

        this.DispatchGeneric(n => n.OnHealthIssue, "OnHealthIssue", payload);
    }

    public void Handle(ApplicationUpdatedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var payload = new
        {
            EventType = "OnApplicationUpdate",
            PreviousVersion = message.PreviousVersion ?? string.Empty,
            NewVersion = message.NewVersion ?? string.Empty,
            Message = $"Leecharr updated to version {message.NewVersion}",
            Timestamp = DateTime.UtcNow,
        };

        this.DispatchGeneric(n => n.OnApplicationUpdate, "OnApplicationUpdate", payload);
    }

    public static string ResolveTargetUrl(string implementation, string settings)
    {
        return NotificationPayloadBuilder.ResolveTargetUrl(implementation, settings);
    }

    public static string ResolveCustomHeaders(string settings)
    {
        return NotificationPayloadBuilder.ResolveCustomHeaders(null, settings);
    }

    public static string ResolveCustomHeaders(string implementation, string settings)
    {
        return NotificationPayloadBuilder.ResolveCustomHeaders(implementation, settings);
    }

    public static string EscapeMarkdown(string text)
    {
        return EpisodicParser.EscapeMarkdownStatic(text);
    }

    public static void SendEmailNotification(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Action<System.Net.Mail.SmtpClient, System.Net.Mail.MailMessage> smtpSender = null)
    {
        EmailNotificationSender.SendEmailNotification(settings, eventType, torrent, (object)meta, genericPayload, smtpSender);
    }

    internal static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload, string settings = null)
    {
        return NotificationPayloadBuilder.BuildProviderPayload(implementation, eventType, torrent, (object)meta, genericPayload, settings);
    }

    private void DispatchGeneric(Func<NotificationDefinition, bool> predicate, string eventType, object payload)
    {
        var activeNotifications = this.notificationRepository.GetEnabled().Where(predicate).ToList();
        if (activeNotifications.Count == 0)
        {
            return;
        }

        foreach (var notif in activeNotifications)
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await this.customScriptService.ExecuteScriptAsync(scriptPath, null, eventType, scriptArgs).ConfigureAwait(false);
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
                        EmailNotificationSender.SendEmailNotification(notif.Settings, eventType, null, null, payload);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error sending email notification for {0}", eventType);
                    }
                });
            }
            else
            {
                var providerPayload = NotificationPayloadBuilder.BuildProviderPayload(notif.Implementation, eventType, null, null, payload, notif.Settings);
                var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(notif.Implementation, notif.Settings);
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

        var (seasonNum, epNum, epTitle) = this.episodicParser.ExtractEpisodicInfo(torrent.Name);
        var (container, resolution, videoCodec, hdrFormat, audioCodec, audioChannels, audioLanguage, subtitleLanguages) = this.episodicParser.ExtractStreamSpecs(meta?.MediaInfoJson);

        var downloadTimeSeconds = torrent.DateCompleted.HasValue && torrent.DateAdded != default && torrent.DateCompleted.Value >= torrent.DateAdded
            ? (long)(torrent.DateCompleted.Value - torrent.DateAdded).TotalSeconds
            : 0L;

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
                etaString = this.episodicParser.FormatEta(torrent.Eta),
                seeders = torrent.Seeders,
                leechers = torrent.Leechers,
                ratio = double.IsFinite(torrent.Ratio) ? torrent.Ratio : 0.0,
                dateAdded = torrent.DateAdded != default ? torrent.DateAdded.ToString("o") : null,
                dateCompleted = torrent.DateCompleted?.ToString("o"),
                downloadTimeSeconds,
                seedingTimeSeconds = torrent.CumulativeSeedingTimeSeconds,
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
                        EmailNotificationSender.SendEmailNotification(notif.Settings, eventType, torrent, meta, payload);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error sending email notification for {0}", eventType);
                    }
                });
            }
            else
            {
                var providerPayload = NotificationPayloadBuilder.BuildProviderPayload(notif.Implementation, eventType, torrent, meta, payload, notif.Settings);
                var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(notif.Implementation, notif.Settings);
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
}
