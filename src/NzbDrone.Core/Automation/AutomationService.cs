#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Automation;

public interface IExtractorService
{
    Task<bool> ExtractAsync(Torrent torrent, string? destination = null, bool deleteArchive = false);
}

public class AutomationService : IAutomationService
{
    private readonly IAutomationScriptRepository _scriptRepository;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IEventAggregator _eventAggregator;
    private readonly IManageCommandQueue? _commandQueue;
    private readonly ICustomScriptService? _customScriptService;
    private readonly INotificationRepository? _notificationRepository;
    private readonly IWebhookDispatcher? _webhookDispatcher;
    private readonly IDownloadEngine? _downloadEngine;
    private readonly IBlocklistService? _blocklistService;
    private readonly ITrackerBoostService? _trackerBoostService;
    private readonly IExtractorService? _extractorService;
    private readonly IDiskProvider? _diskProvider;
    private readonly IArchiveExtractorService? _archiveExtractorService;
    private readonly ITorrentService? _torrentService;
    private readonly ITrackerEntryRepository? _trackerEntryRepository;
    private readonly ITorrentFileService? _torrentFileService;
    private readonly IAppFolderInfo? _appFolderInfo;
    private readonly Logger _logger;
    private readonly JintScriptRunner _jintRunner;
    private readonly YamlScriptRunner _yamlRunner;

    public AutomationService(
        IAutomationScriptRepository scriptRepository,
        ITorrentRepository torrentRepository,
        ITagRepository tagRepository,
        IEventAggregator eventAggregator,
        IManageCommandQueue? commandQueue = null,
        IConfigFileProvider? configFileProvider = null,
        ICustomScriptService? customScriptService = null,
        INotificationRepository? notificationRepository = null,
        IWebhookDispatcher? webhookDispatcher = null,
        IDownloadEngine? downloadEngine = null,
        IBlocklistService? blocklistService = null,
        ITrackerBoostService? trackerBoostService = null,
        IExtractorService? extractorService = null,
        IDiskProvider? diskProvider = null,
        IArchiveExtractorService? archiveExtractorService = null,
        ITorrentService? torrentService = null,
        ITrackerEntryRepository? trackerEntryRepository = null,
        ITorrentFileService? torrentFileService = null,
        IAppFolderInfo? appFolderInfo = null)
    {
        _scriptRepository = scriptRepository;
        _torrentRepository = torrentRepository;
        _tagRepository = tagRepository;
        _eventAggregator = eventAggregator;
        _commandQueue = commandQueue;
        _customScriptService = customScriptService;
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _downloadEngine = downloadEngine;
        _blocklistService = blocklistService;
        _trackerBoostService = trackerBoostService;
        _extractorService = extractorService;
        _diskProvider = diskProvider;
        _archiveExtractorService = archiveExtractorService;
        _torrentService = torrentService;
        _trackerEntryRepository = trackerEntryRepository;
        _torrentFileService = torrentFileService;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
        _jintRunner = new JintScriptRunner(commandQueue, configFileProvider);
        _yamlRunner = new YamlScriptRunner(commandQueue);
    }

    public List<AutomationScript> GetAll()
    {
        return _scriptRepository.All().ToList();
    }

    public AutomationScript Get(int id)
    {
        return _scriptRepository.Get(id);
    }

    public AutomationScript Add(AutomationScript script)
    {
        script.CreatedAt = DateTime.UtcNow;
        var added = _scriptRepository.Insert(script);
        _eventAggregator.PublishEvent(new ModelEvent<AutomationScript>(added, ModelAction.Created));
        return added;
    }

    public AutomationScript Update(AutomationScript script)
    {
        var updated = _scriptRepository.Update(script);
        _eventAggregator.PublishEvent(new ModelEvent<AutomationScript>(updated, ModelAction.Updated));
        return updated;
    }

    public void Delete(int id)
    {
        var existing = _scriptRepository.Get(id);
        if (existing != null)
        {
            _scriptRepository.Delete(id);
            _eventAggregator.PublishEvent(new ModelEvent<AutomationScript>(existing, ModelAction.Deleted));
        }
    }

    public AutomationExecutionResult ExecuteScript(int scriptId, int? torrentId = null, Dictionary<string, object>? customInputs = null)
    {
        var script = _scriptRepository.Get(scriptId);
        if (script == null)
        {
            return new AutomationExecutionResult
            {
                Success = false,
                Error = $"Script with ID {scriptId} not found.",
            };
        }

        Torrent? torrent = null;
        if (torrentId.HasValue && torrentId.Value > 0)
        {
            torrent = _torrentRepository.Get(torrentId.Value);
        }

        return ExecuteScript(script, torrent, customInputs);
    }

    public AutomationExecutionResult ExecuteScript(AutomationScript script, Torrent? torrent = null, Dictionary<string, object>? customInputs = null)
    {
        _logger.Info("Executing automation script '{0}' (Trigger: {1}, Language: {2})", script.Name, script.Trigger, script.Language);

        var torrentTags = new List<string>();
        if (torrent != null && torrent.TagIds != null && torrent.TagIds.Count > 0)
        {
            var allTags = _tagRepository.All().ToList();
            var tagMap = allTags.ToDictionary(t => t.Id, t => t.Label);
            foreach (var tid in torrent.TagIds)
            {
                if (tagMap.TryGetValue(tid, out var label))
                {
                    torrentTags.Add(label);
                }
            }
        }

        IScriptRunner runner = script.Language == AutomationLanguage.Yaml ? _yamlRunner : _jintRunner;
        var result = runner.Execute(script, torrent, torrentTags, customInputs);

        // Apply mutations if torrent is present and execution was successful
        if (result.Success && torrent != null)
        {
            ApplyTorrentMutations(torrent, result);
        }

        // Side-effects: Servarr Sync
        if (result.Success && result.ArrSyncsToSend.Count > 0 && _commandQueue != null)
        {
            foreach (var sync in result.ArrSyncsToSend)
            {
                _commandQueue.Push(
                    new SyncArrCommand
                    {
                        AppType = sync.AppType,
                        InstanceId = sync.InstanceId,
                    },
                    CommandTrigger.Manual);
            }
        }

        // Side-effects: Custom scripts to run
        if (result.Success && result.ScriptsToRun.Count > 0 && _customScriptService != null)
        {
            foreach (var scriptToRun in result.ScriptsToRun)
            {
                var timeout = scriptToRun.TimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(scriptToRun.TimeoutSeconds)
                    : (TimeSpan?)null;

                Task.Run(async () =>
                {
                    try
                    {
                        await _customScriptService.ExecuteScriptAsync(
                            scriptToRun.Path,
                            torrent,
                            "Automation",
                            scriptToRun.Arguments,
                            timeout).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to execute custom script from automation: {0}", scriptToRun.Path);
                    }
                });
            }
        }

        // Side-effects: Notifications to send
        if (result.Success && result.NotificationsToSend.Count > 0 && _notificationRepository != null)
        {
            var activeNotifications = _notificationRepository.GetEnabled();
            foreach (var notif in result.NotificationsToSend)
            {
                var matching = string.IsNullOrWhiteSpace(notif.Provider)
                    ? activeNotifications
                    : activeNotifications.Where(n =>
                        string.Equals(n.Implementation, notif.Provider, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(n.Name, notif.Provider, StringComparison.OrdinalIgnoreCase)).ToList();

                var genericPayload = new { title = notif.Title, message = notif.Message };

                foreach (var n in matching)
                {
                    if (string.Equals(n.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_customScriptService == null)
                        {
                            continue;
                        }

                        var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(n.Settings);
                        Task.Run(async () =>
                        {
                            try
                            {
                                await _customScriptService.ExecuteScriptAsync(scriptPath, torrent, "Automation", scriptArgs).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to execute custom script notification from automation");
                            }
                        });
                    }
                    else if (string.Equals(n.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                await EmailNotificationSender.SendEmailNotificationAsync(n.Settings, "Automation", torrent, null, genericPayload).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to send email notification from automation");
                            }
                        });
                    }
                    else if (_webhookDispatcher != null)
                    {
                        var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(n.Implementation, n.Settings);
                        var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(n.Implementation, n.Settings);
                        var httpMethod = NotificationPayloadBuilder.ResolveHttpMethod(n.Implementation, n.Settings);
                        var payload = NotificationPayloadBuilder.BuildProviderPayload(n.Implementation, "Automation", torrent, null, genericPayload, n.Settings);

                        Task.Run(async () =>
                        {
                            try
                            {
                                await _webhookDispatcher.DispatchAsync(targetUrl, payload, customHeaders, httpMethod).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to dispatch automation notification via {0}", n.Implementation);
                            }
                        });
                    }
                }
            }
        }

        // Record execution stats on script
        script.LastExecutedAt = DateTime.UtcNow;
        script.LastExecutionStatus = result.Success ? "Success" : "Failed";
        script.LastExecutionLog = result.OutputLog ?? result.Error;

        if (script.Id > 0)
        {
            try
            {
                _scriptRepository.Update(script);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to update last execution stats for script {0}", script.Name);
            }
        }

        return result;
    }

    public AutomationExecutionResult TestScript(AutomationScript script, int? torrentId = null, Dictionary<string, object>? customInputs = null)
    {
        Torrent? torrent = null;
        if (torrentId.HasValue && torrentId.Value > 0)
        {
            torrent = _torrentRepository.Get(torrentId.Value);
        }
        else
        {
            torrent = _torrentRepository.All().FirstOrDefault();
        }

        var torrentTags = new List<string>();
        if (torrent != null && torrent.TagIds != null && torrent.TagIds.Count > 0)
        {
            var allTags = _tagRepository.All().ToList();
            var tagMap = allTags.ToDictionary(t => t.Id, t => t.Label);
            foreach (var tid in torrent.TagIds)
            {
                if (tagMap.TryGetValue(tid, out var label))
                {
                    torrentTags.Add(label);
                }
            }
        }
        else if (torrent == null)
        {
            torrent = new Torrent
            {
                Id = 1,
                Name = "Simulated.Linux.Ubuntu.24.04.LTS.iso",
                InfoHash = "0123456789abcdef0123456789abcdef01234567",
                TotalSize = 4_294_967_296L,
                Ratio = 2.45,
                Progress = 1.0f,
                Status = TorrentStatus.Seeding,
                Category = "Linux",
                TrackerUrl = "https://torrent.ubuntu.com/announce",
                DownloadSpeed = 0,
                UploadSpeed = 5_242_880,
                Seeders = 125,
                Leechers = 14,
                SavePath = "/downloads/completed/Linux",
                Downloaded = 4_294_967_296L,
                Uploaded = 10_522_670_875L,
                CumulativeSeedingTimeSeconds = 172800,
                IsPrivate = false,
            };
            torrentTags.Add("Simulated");
            torrentTags.Add("Verified");
        }

        IScriptRunner runner = script.Language == AutomationLanguage.Yaml ? _yamlRunner : _jintRunner;
        return runner.Execute(script, torrent, torrentTags, customInputs, dryRun: true);
    }

    private void ApplyTorrentMutations(Torrent torrent, AutomationExecutionResult result)
    {
        ApplyExecutionResultAsync(torrent, result).GetAwaiter().GetResult();
    }

    private Task ApplyTorrentMutationsAsync(Torrent torrent, AutomationExecutionResult result)
    {
        return ApplyExecutionResultAsync(torrent, result);
    }

    public async Task ApplyExecutionResultAsync(Torrent torrent, AutomationExecutionResult result)
    {
        ExecuteAutomationActions(torrent, result);

        var changed = false;

        // Tags to add
        if (result.TagsToAdd.Count > 0)
        {
            var allTags = _tagRepository.All().ToList();
            var tagLookup = allTags.ToDictionary(t => t.Label, t => t.Id, StringComparer.OrdinalIgnoreCase);

            torrent.TagIds ??= new List<int>();
            foreach (var tagLabel in result.TagsToAdd)
            {
                if (!tagLookup.TryGetValue(tagLabel, out var tagId))
                {
                    var created = _tagRepository.Insert(new Tag { Label = tagLabel });
                    tagId = created.Id;
                    tagLookup[tagLabel] = tagId;
                }

                if (!torrent.TagIds.Contains(tagId))
                {
                    torrent.TagIds.Add(tagId);
                    changed = true;
                }
            }
        }

        // Tags to remove
        if (result.TagsToRemove.Count > 0 && torrent.TagIds != null && torrent.TagIds.Count > 0)
        {
            var allTags = _tagRepository.All().ToList();
            var tagLookup = allTags.ToDictionary(t => t.Label, t => t.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var tagLabel in result.TagsToRemove)
            {
                if (tagLookup.TryGetValue(tagLabel, out var tagId))
                {
                    if (torrent.TagIds.Remove(tagId))
                    {
                        changed = true;
                    }
                }
            }
        }

        // Category change
        if (!string.IsNullOrWhiteSpace(result.NewCategory) && !string.Equals(torrent.Category, result.NewCategory, StringComparison.OrdinalIgnoreCase))
        {
            torrent.Category = result.NewCategory;
            changed = true;
        }

        string? pendingSavePath = null;
        if (!string.IsNullOrWhiteSpace(result.NewSavePath)
            && !string.Equals(torrent.SavePath, result.NewSavePath, StringComparison.OrdinalIgnoreCase))
        {
            pendingSavePath = result.NewSavePath.Trim();
        }

        // Upload limit
        if (result.NewUploadLimitKbps.HasValue && torrent.UploadLimit != result.NewUploadLimitKbps.Value)
        {
            torrent.UploadLimit = result.NewUploadLimitKbps.Value;
            changed = true;
        }

        // Download limit
        if (result.NewDownloadLimitKbps.HasValue && torrent.DownloadLimit != result.NewDownloadLimitKbps.Value)
        {
            torrent.DownloadLimit = result.NewDownloadLimitKbps.Value;
            changed = true;
        }

        // Priority
        if (result.NewPriority.HasValue && torrent.Priority != result.NewPriority.Value)
        {
            torrent.Priority = result.NewPriority.Value;
            changed = true;
        }

        // Sequential download
        if (result.NewSequentialDownload.HasValue && torrent.SequentialDownload != result.NewSequentialDownload.Value)
        {
            torrent.SequentialDownload = result.NewSequentialDownload.Value;
            changed = true;
        }

        // Initial/Super seeding
        if (result.NewSuperSeeding.HasValue && torrent.InitialSeeding != result.NewSuperSeeding.Value)
        {
            torrent.InitialSeeding = result.NewSuperSeeding.Value;
            changed = true;
        }

        // Ratio limit
        if (result.NewRatioLimit.HasValue && Math.Abs(torrent.TargetRatio - result.NewRatioLimit.Value) > 0.001)
        {
            torrent.TargetRatio = result.NewRatioLimit.Value;
            changed = true;
        }

        // Seeding time limit
        if (result.NewSeedingTimeLimitMinutes.HasValue && torrent.TargetSeedTimeMinutes != result.NewSeedingTimeLimitMinutes.Value)
        {
            torrent.TargetSeedTimeMinutes = result.NewSeedingTimeLimitMinutes.Value;
            changed = true;
        }

        // Share limit action
        if (!string.IsNullOrWhiteSpace(result.ShareLimitAction) && !string.Equals(torrent.ShareLimitAction, result.ShareLimitAction, StringComparison.OrdinalIgnoreCase))
        {
            torrent.ShareLimitAction = result.ShareLimitAction;
            changed = true;
        }

        // Tracker add
        if (result.TrackersToAdd.Count > 0)
        {
            var urlsToAdd = result.TrackersToAdd
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (urlsToAdd.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(torrent.TrackerUrl))
                {
                    torrent.TrackerUrl = urlsToAdd[0];
                    changed = true;
                }

                var engineUrls = urlsToAdd;
                if (_trackerEntryRepository != null)
                {
                    var existingUrls = _trackerEntryRepository.GetByTorrentId(torrent.Id)
                        .Where(e => !string.IsNullOrWhiteSpace(e.Url))
                        .Select(e => e.Url.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var now = DateTime.UtcNow;
                    var newlyAdded = new List<string>();
                    foreach (var url in urlsToAdd)
                    {
                        if (existingUrls.Contains(url))
                        {
                            continue;
                        }

                        _trackerEntryRepository.Insert(new TrackerEntry
                        {
                            TorrentId = torrent.Id,
                            Url = url,
                            Tier = 0,
                            Enabled = true,
                            Status = 0,
                            AnnounceInterval = 1800,
                            NextAnnounce = now.AddSeconds(1800),
                        });
                        existingUrls.Add(url);
                        newlyAdded.Add(url);
                    }

                    engineUrls = newlyAdded;
                }

                if (_downloadEngine != null && engineUrls.Count > 0)
                {
                    try
                    {
                        await _downloadEngine.AddTrackersAsync(torrent.Id, engineUrls).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to add trackers to torrent {0}", torrent.Id);
                    }
                }
            }
        }

        // Tracker replacement
        if (result.TrackersToReplace != null && result.TrackersToReplace.Count > 0)
        {
            foreach (var trackerPair in result.TrackersToReplace)
            {
                var oldTracker = trackerPair.Item1;
                var newTracker = trackerPair.Item2;

                if (!string.IsNullOrWhiteSpace(oldTracker) && !string.IsNullOrWhiteSpace(newTracker) && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
                {
                    if (string.Equals(torrent.TrackerUrl, oldTracker, StringComparison.OrdinalIgnoreCase))
                    {
                        torrent.TrackerUrl = newTracker;
                        changed = true;
                    }
                    else if (torrent.TrackerUrl.Contains(oldTracker))
                    {
                        torrent.TrackerUrl = torrent.TrackerUrl.Replace(oldTracker, newTracker);
                        changed = true;
                    }
                }
            }

            if (_trackerEntryRepository != null)
            {
                var entries = _trackerEntryRepository.GetByTorrentId(torrent.Id).ToList();
                foreach (var entry in entries)
                {
                    foreach (var trackerPair in result.TrackersToReplace)
                    {
                        if (!string.IsNullOrWhiteSpace(trackerPair.Item1) &&
                            !string.IsNullOrWhiteSpace(trackerPair.Item2) &&
                            string.Equals(entry.Url, trackerPair.Item1, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Url = trackerPair.Item2;
                            _trackerEntryRepository.Update(entry);
                        }
                    }
                }
            }

            if (_downloadEngine != null)
            {
                foreach (var trackerPair in result.TrackersToReplace)
                {
                    if (!string.IsNullOrWhiteSpace(trackerPair.Item1) && !string.IsNullOrWhiteSpace(trackerPair.Item2))
                    {
                        try
                        {
                            await _downloadEngine.RemoveTrackersAsync(torrent.Id, new[] { trackerPair.Item1 }).ConfigureAwait(false);
                            await _downloadEngine.AddTrackersAsync(torrent.Id, new[] { trackerPair.Item2 }).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to replace tracker {0} with {1} on torrent {2}", trackerPair.Item1, trackerPair.Item2, torrent.Id);
                        }
                    }
                }
            }
        }

        // Tracker removal
        if (result.TrackersToRemove.Count > 0)
        {
            var removalPatterns = result.TrackersToRemove
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (removalPatterns.Count > 0)
            {
                var urlsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
                {
                    foreach (var pattern in removalPatterns)
                    {
                        if (TrackerUrlMatches(pattern, torrent.TrackerUrl))
                        {
                            urlsToRemove.Add(torrent.TrackerUrl.Trim());
                            torrent.TrackerUrl = null;
                            changed = true;
                            break;
                        }
                    }
                }

                if (_trackerEntryRepository != null)
                {
                    foreach (var entry in _trackerEntryRepository.GetByTorrentId(torrent.Id).ToList())
                    {
                        if (string.IsNullOrWhiteSpace(entry.Url))
                        {
                            continue;
                        }

                        foreach (var pattern in removalPatterns)
                        {
                            if (TrackerUrlMatches(pattern, entry.Url))
                            {
                                urlsToRemove.Add(entry.Url.Trim());
                                _trackerEntryRepository.Delete(entry.Id);
                                break;
                            }
                        }
                    }
                }

                if (_downloadEngine != null && urlsToRemove.Count > 0)
                {
                    try
                    {
                        await _downloadEngine.RemoveTrackersAsync(torrent.Id, urlsToRemove).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to remove trackers from torrent {0}", torrent.Id);
                    }
                }
            }
        }

        // Status mutations (pause, resume, remove)
        if (result.ShouldRemove)
        {
            _logger.Info("Automation script requested removal of torrent '{0}' (deleteData={1})", torrent.Name, result.DeleteDataOnRemove);
            if (_torrentService != null)
            {
                await _torrentService.DeleteAsync(torrent.Id, result.DeleteDataOnRemove).ConfigureAwait(false);
            }
            else
            {
                _torrentRepository.Delete(torrent.Id);
                _eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = result.DeleteDataOnRemove });
                _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Deleted));
            }

            return;
        }

        var oldStatus = torrent.Status;
        if (result.ShouldPause && torrent.Status != TorrentStatus.Paused)
        {
            this._logger.Info("[State Machine] Torrent #{0} ('{1}') auto-paused by automation rule/action", torrent.Id, torrent.Name);
            if (_torrentService != null)
            {
                await _torrentService.PauseAsync(torrent.Id).ConfigureAwait(false);
                torrent.Status = TorrentStatus.Paused;
            }
            else
            {
                torrent.Status = TorrentStatus.Paused;
                changed = true;
                _eventAggregator.PublishEvent(new TorrentPausedEvent(torrent));
                _eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                {
                    Torrent = torrent,
                    OldStatus = oldStatus,
                    NewStatus = TorrentStatus.Paused,
                    Reason = "Auto-paused by automation rule/action",
                });
            }
        }
        else if (result.ShouldResume && torrent.Status == TorrentStatus.Paused)
        {
            this._logger.Info("[State Machine] Torrent #{0} ('{1}') auto-resumed by automation rule/action", torrent.Id, torrent.Name);
            if (_torrentService != null)
            {
                await _torrentService.ResumeAsync(torrent.Id).ConfigureAwait(false);
                torrent.Status = (torrent.Progress >= 1.0f || torrent.DateCompleted != null)
                    ? TorrentStatus.Seeding
                    : TorrentStatus.Downloading;
            }
            else
            {
                torrent.Status = (torrent.Progress >= 1.0f || torrent.DateCompleted != null)
                    ? TorrentStatus.Seeding
                    : TorrentStatus.Downloading;
                changed = true;
                _eventAggregator.PublishEvent(new TorrentStartedEvent(torrent));
                _eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                {
                    Torrent = torrent,
                    OldStatus = oldStatus,
                    NewStatus = torrent.Status,
                    Reason = "Auto-resumed by automation rule/action",
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(pendingSavePath))
        {
            if (_torrentService != null)
            {
                await _torrentService.SetLocationAsync(torrent.Id, pendingSavePath, moveFiles: true).ConfigureAwait(false);
                torrent.SavePath = pendingSavePath;
            }
            else
            {
                torrent.SavePath = pendingSavePath;
                changed = true;
            }
        }

        if (changed)
        {
            if (_torrentService != null)
            {
                await _torrentService.UpdateAsync(torrent).ConfigureAwait(false);

                if (result.NewSuperSeeding.HasValue)
                {
                    try
                    {
                        await _torrentService.SetSuperSeedingAsync(torrent.Id, torrent.InitialSeeding).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to apply super seeding to torrent {0} via automation", torrent.Id);
                    }
                }
            }
            else
            {
                _torrentRepository.Update(torrent);
            }

            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        }

        // Force recheck
        if (result.ShouldRecheck)
        {
            if (_torrentService != null)
            {
                await _torrentService.ForceRecheckAsync(torrent.Id).ConfigureAwait(false);
            }
            else if (_downloadEngine != null)
            {
                await _downloadEngine.ForceRecheckAsync(torrent.Id).ConfigureAwait(false);
            }
        }

        // Force reannounce
        if (result.ShouldReannounce || result.ShouldReannounceAll)
        {
            if (_torrentService != null)
            {
                await _torrentService.ForceAnnounceAsync(torrent.Id).ConfigureAwait(false);
            }
            else if (_downloadEngine != null)
            {
                await _downloadEngine.ForceAnnounceAsync(torrent.Id).ConfigureAwait(false);
            }
        }

        if (result.FilePriorities.Count > 0)
        {
            await ApplyFilePriorityRulesAsync(torrent, result.FilePriorities).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(result.TorrentExportDestination))
        {
            await ExportTorrentFileAsync(torrent, result.TorrentExportDestination.Trim()).ConfigureAwait(false);
        }
    }

    private void ExecuteAutomationActions(Torrent torrent, AutomationExecutionResult result)
    {
        // 1. Peers to ban
        if (result.PeersToBan.Count > 0)
        {
            foreach (var ip in result.PeersToBan)
            {
                if (string.IsNullOrWhiteSpace(ip))
                {
                    continue;
                }

                if (_blocklistService != null)
                {
                    try
                    {
                        var task = _blocklistService.AddRulesAsync(new[] { ip });
                        if (task != null)
                        {
                            task.ContinueWith(
                                t =>
                                {
                                    if (t.IsFaulted && t.Exception != null)
                                    {
                                        _logger.Error(t.Exception.GetBaseException(), "Failed to add peer {0} to blocklist", ip);
                                    }
                                },
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted,
                                TaskScheduler.Default);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to ban peer IP {0} via blocklist service", ip);
                    }
                }

                if (_downloadEngine != null)
                {
                    try
                    {
                        var task = _downloadEngine.DisconnectPeerAsync(ip);
                        if (task != null)
                        {
                            task.ContinueWith(
                                t =>
                                {
                                    if (t.IsFaulted && t.Exception != null)
                                    {
                                        _logger.Error(t.Exception.GetBaseException(), "Failed to disconnect peer {0} on torrent {1}", ip, torrent.Id);
                                    }
                                },
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted,
                                TaskScheduler.Default);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to disconnect peer {0} on torrent {1}", ip, torrent.Id);
                    }
                }
            }
        }

        // 2. Boost Tracker
        if (result.ShouldBoostTracker)
        {
            try
            {
                if (_trackerBoostService != null)
                {
                    var boostMethod = _trackerBoostService.GetType().GetMethod("BoostAsync", new[] { typeof(int) });
                    if (boostMethod != null)
                    {
                        var ret = boostMethod.Invoke(_trackerBoostService, new object[] { torrent.Id });
                        if (ret is Task task)
                        {
                            task.ContinueWith(
                                t =>
                                {
                                    if (t.IsFaulted && t.Exception != null)
                                    {
                                        _logger.Error(t.Exception.GetBaseException(), "Failed to boost tracker for torrent {0}", torrent.Name);
                                    }
                                },
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted,
                                TaskScheduler.Default);
                        }
                    }
                    else
                    {
                        var task = _trackerBoostService.BoostTorrentAsync(torrent.Id);
                        if (task != null)
                        {
                            task.ContinueWith(
                                t =>
                                {
                                    if (t.IsFaulted && t.Exception != null)
                                    {
                                        _logger.Error(t.Exception.GetBaseException(), "Failed to boost tracker for torrent {0}", torrent.Name);
                                    }
                                },
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted,
                                TaskScheduler.Default);
                        }
                    }
                }
                else if (_downloadEngine != null)
                {
                    var task = _downloadEngine.ForceAnnounceAsync(torrent.Id);
                    if (task != null)
                    {
                        task.ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted && t.Exception != null)
                                {
                                    _logger.Error(t.Exception.GetBaseException(), "Failed to force announce for torrent {0}", torrent.Name);
                                }
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted,
                            TaskScheduler.Default);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to boost tracker for torrent {0}", torrent.Name);
            }
        }

        // 3. Extract Archive
        if (result.ShouldExtractArchive)
        {
            if (_archiveExtractorService != null)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await ExtractArchiveWithServiceAsync(torrent, result.ExtractDestination, result.DeleteArchiveOnExtract).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to extract archive for torrent {0}", torrent.Name);
                    }
                });
            }
            else if (_extractorService != null)
            {
                try
                {
                    var task = _extractorService.ExtractAsync(torrent, result.ExtractDestination, result.DeleteArchiveOnExtract);
                    if (task != null)
                    {
                        task.ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted && t.Exception != null)
                                {
                                    _logger.Error(t.Exception.GetBaseException(), "Failed to extract archive for torrent {0}", torrent.Name);
                                }
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted,
                            TaskScheduler.Default);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to extract archive for torrent {0}", torrent.Name);
                }
            }
        }

        // 4. Clean Unwanted Files
        if (result.CleanFilePatterns.Count > 0 && !string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            try
            {
                CleanUnwantedFiles(torrent.SavePath, result.CleanFilePatterns);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to clean unwanted files for torrent {0}", torrent.Name);
            }
        }
    }

    private void CleanUnwantedFiles(string rootPath, List<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || patterns == null || patterns.Count == 0)
        {
            return;
        }

        IEnumerable<string> files;
        if (_diskProvider != null)
        {
            if (_diskProvider.FolderExists(rootPath))
            {
                files = _diskProvider.GetFiles(rootPath, recursive: true);
            }
            else if (_diskProvider.FileExists(rootPath))
            {
                files = new[] { rootPath };
            }
            else
            {
                return;
            }
        }
        else
        {
            if (Directory.Exists(rootPath))
            {
                files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
            }
            else if (File.Exists(rootPath))
            {
                files = new[] { rootPath };
            }
            else
            {
                return;
            }
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var matched = false;

            foreach (var pattern in patterns)
            {
                if (MatchesPattern(fileName, pattern))
                {
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                try
                {
                    _logger.Info("Deleting unwanted file {0} matching automation pattern", file);
                    if (_diskProvider != null)
                    {
                        _diskProvider.DeleteFile(file);
                    }
                    else
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to delete unwanted file {0}", file);
                }
            }
        }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var trimmed = pattern.Trim();
        if (trimmed.Contains('*') || trimmed.Contains('?'))
        {
            var regexPattern = "^" + Regex.Escape(trimmed).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        }

        return string.Equals(fileName, trimmed, StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("." + trimmed.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrackerUrlMatches(string pattern, string trackerUrl)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(trackerUrl))
        {
            return false;
        }

        var trimmedPattern = pattern.Trim();
        var trimmedUrl = trackerUrl.Trim();
        if (string.Equals(trimmedUrl, trimmedPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmedPattern.Contains('*', StringComparison.Ordinal) || trimmedPattern.Contains('?', StringComparison.Ordinal))
        {
            return MatchesPattern(trimmedUrl, trimmedPattern);
        }

        return trimmedUrl.Contains(trimmedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseAutomationFilePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return 3;
        }

        if (int.TryParse(priority, out var parsed))
        {
            return Math.Clamp(parsed, 0, 7);
        }

        if (string.Equals(priority, "donotdownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(priority, "off", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(priority, "high", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (string.Equals(priority, "low", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private async Task ApplyFilePriorityRulesAsync(Torrent torrent, List<Tuple<string, string>> rules)
    {
        if (_torrentFileService == null || rules == null || rules.Count == 0)
        {
            return;
        }

        var files = _torrentFileService.GetFiles(torrent.Id).ToList();
        if (files.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Item1))
            {
                continue;
            }

            var pattern = rule.Item1.Trim();
            var priority = ParseAutomationFilePriority(rule.Item2);
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file.Path))
                {
                    continue;
                }

                var fileName = Path.GetFileName(file.Path);
                var normalizedPath = file.Path.Replace('\\', '/');
                if (!MatchesPattern(fileName, pattern) && !MatchesPattern(normalizedPath, pattern))
                {
                    continue;
                }

                try
                {
                    await _torrentFileService.SetPriorityAsync(file.Id, priority).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to set file priority for {0} on torrent {1}", file.Path, torrent.Id);
                }
            }
        }
    }

    private async Task ExportTorrentFileAsync(Torrent torrent, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            _logger.Warn("Cannot export torrent {0}: missing info hash", torrent.Id);
            return;
        }

        var sourcePath = ResolveCachedTorrentPath(torrent.InfoHash);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            _logger.Warn("Cannot export torrent {0}: cached .torrent file not found", torrent.Id);
            return;
        }

        try
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                if (_diskProvider != null)
                {
                    _diskProvider.CreateFolder(destinationDirectory);
                }
                else
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
            }

            if (_diskProvider != null)
            {
                _diskProvider.CopyFile(sourcePath, destinationPath, overwrite: true);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export torrent {0} to {1}", torrent.Id, destinationPath);
        }
    }

    private string? ResolveCachedTorrentPath(string infoHash)
    {
        var hash = infoHash.Trim().ToLowerInvariant();
        var pathsToTry = new List<string>();

        if (_appFolderInfo != null && !string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            pathsToTry.Add(Path.Combine(_appFolderInfo.AppDataFolder, "Torrents", $"{hash}.torrent"));
            pathsToTry.Add(Path.Combine(_appFolderInfo.AppDataFolder, "Leecharr", "Torrents", $"{hash}.torrent"));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            pathsToTry.Add(Path.Combine(appData, "Torrents", $"{hash}.torrent"));
            pathsToTry.Add(Path.Combine(appData, "Leecharr", "Torrents", $"{hash}.torrent"));
        }

        foreach (var path in pathsToTry)
        {
            if (_diskProvider != null)
            {
                if (_diskProvider.FileExists(path))
                {
                    return path;
                }
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    internal async Task<bool> ExtractArchiveWithServiceAsync(Torrent torrent, string? destination, bool deleteArchive)
    {
        if (_archiveExtractorService == null || string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            return false;
        }

        var rootPath = torrent.SavePath;
        var destDir = !string.IsNullOrWhiteSpace(destination) ? destination : rootPath;

        var allFiles = new List<string>();
        if (_diskProvider != null)
        {
            if (_diskProvider.FolderExists(rootPath))
            {
                allFiles.AddRange(_diskProvider.GetFiles(rootPath, recursive: true));
            }
            else if (_diskProvider.FileExists(rootPath))
            {
                allFiles.Add(rootPath);
                if (string.IsNullOrWhiteSpace(destination))
                {
                    destDir = Path.GetDirectoryName(Path.GetFullPath(rootPath)) ?? rootPath;
                }
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (Directory.Exists(rootPath))
            {
                allFiles.AddRange(Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories));
            }
            else if (File.Exists(rootPath))
            {
                allFiles.Add(rootPath);
                if (string.IsNullOrWhiteSpace(destination))
                {
                    destDir = Path.GetDirectoryName(Path.GetFullPath(rootPath)) ?? rootPath;
                }
            }
            else
            {
                return false;
            }
        }

        var primaryArchives = allFiles
            .Where(f => _archiveExtractorService.IsArchiveFile(f) && !ArchiveExtractorEventHandler.IsSecondaryVolume(f))
            .ToList();

        if (primaryArchives.Count == 0)
        {
            return false;
        }

        var allSuccess = true;
        foreach (var archive in primaryArchives)
        {
            var targetDest = !string.IsNullOrWhiteSpace(destination) ? destination : (Path.GetDirectoryName(archive) ?? destDir);
            var success = await _archiveExtractorService.ExtractArchiveAsync(archive, targetDest).ConfigureAwait(false);
            if (!success)
            {
                allSuccess = false;
                continue;
            }

            if (deleteArchive)
            {
                DeleteArchiveAndSiblings(archive, allFiles);
            }
        }

        return allSuccess;
    }

    private void DeleteArchiveAndSiblings(string archivePath, IEnumerable<string> allFiles)
    {
        var archiveDir = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        var basePrefix = ArchiveExtractorEventHandler.GetArchiveBasePrefix(archivePath);

        var archivesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { archivePath };

        foreach (var file in allFiles)
        {
            var fileDir = Path.GetDirectoryName(Path.GetFullPath(file));
            if (!string.Equals(fileDir, archiveDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            if (!string.IsNullOrEmpty(basePrefix) && fileName.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if ((_archiveExtractorService != null && _archiveExtractorService.IsArchiveFile(file)) ||
                    ArchiveExtractorEventHandler.IsSecondaryVolume(file))
                {
                    archivesToDelete.Add(file);
                }
            }
        }

        foreach (var file in archivesToDelete)
        {
            try
            {
                if (_diskProvider != null)
                {
                    if (_diskProvider.FileExists(file))
                    {
                        _diskProvider.DeleteFile(file);
                    }
                }
                else if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to delete extracted archive volume: {0}", file);
            }
        }
    }
}
