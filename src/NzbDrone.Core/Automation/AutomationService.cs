#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;

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
        IDiskProvider? diskProvider = null)
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
                _commandQueue.PushRaw("SyncArr", "{}", CommandTrigger.Manual);
            }
        }

        // Side-effects: Custom scripts to run
        if (result.Success && result.ScriptsToRun.Count > 0 && _customScriptService != null)
        {
            foreach (var scriptToRun in result.ScriptsToRun)
            {
                var argsStr = scriptToRun.Arguments != null && scriptToRun.Arguments.Count > 0
                    ? string.Join(" ", scriptToRun.Arguments)
                    : null;
                Task.Run(async () =>
                {
                    try
                    {
                        await _customScriptService.ExecuteScriptAsync(scriptToRun.Path, torrent, "Automation", argsStr).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to execute custom script from automation: {0}", scriptToRun.Path);
                    }
                });
            }
        }

        // Side-effects: Notifications to send
        if (result.Success && result.NotificationsToSend.Count > 0 && _notificationRepository != null && _webhookDispatcher != null)
        {
            var activeNotifications = _notificationRepository.GetEnabled();
            foreach (var notif in result.NotificationsToSend)
            {
                var matching = string.IsNullOrWhiteSpace(notif.Provider)
                    ? activeNotifications
                    : activeNotifications.Where(n =>
                        string.Equals(n.Implementation, notif.Provider, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(n.Name, notif.Provider, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var n in matching)
                {
                    var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(n.Implementation, n.Settings);
                    var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(n.Implementation, n.Settings);
                    var payload = NotificationPayloadBuilder.BuildProviderPayload(n.Implementation, "Automation", torrent, null, new { title = notif.Title, message = notif.Message }, n.Settings);

                    Task.Run(async () =>
                    {
                        try
                        {
                            await _webhookDispatcher.DispatchAsync(targetUrl, payload, customHeaders).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to dispatch automation notification via {0}", n.Implementation);
                        }
                    });
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
        return runner.Execute(script, torrent, torrentTags, customInputs);
    }

    private void ApplyTorrentMutations(Torrent torrent, AutomationExecutionResult result)
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

        // Save Path
        if (!string.IsNullOrWhiteSpace(result.NewSavePath) && !string.Equals(torrent.SavePath, result.NewSavePath, StringComparison.OrdinalIgnoreCase))
        {
            torrent.SavePath = result.NewSavePath;
            changed = true;
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

        // Tracker add/remove
        if (result.TrackersToAdd.Count > 0 && string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            torrent.TrackerUrl = result.TrackersToAdd[0];
            changed = true;
        }

        // Status mutations (pause, resume, remove)
        if (result.ShouldRemove)
        {
            _logger.Info("Automation script requested removal of torrent '{0}' (deleteData={1})", torrent.Name, result.DeleteDataOnRemove);
            _torrentRepository.Delete(torrent.Id);
            _eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = result.DeleteDataOnRemove });
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Deleted));
            return;
        }

        var oldStatus = torrent.Status;
        if (result.ShouldPause && torrent.Status != TorrentStatus.Paused)
        {
            torrent.Status = TorrentStatus.Paused;
            changed = true;
            _eventAggregator.PublishEvent(new TorrentPausedEvent(torrent));
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent
            {
                Torrent = torrent,
                OldStatus = oldStatus,
                NewStatus = TorrentStatus.Paused,
            });
        }
        else if (result.ShouldResume && torrent.Status == TorrentStatus.Paused)
        {
            torrent.Status = (torrent.Progress >= 1.0f || torrent.Progress >= 0.999f || torrent.DateCompleted != null)
                ? TorrentStatus.Seeding
                : TorrentStatus.Downloading;
            changed = true;
            _eventAggregator.PublishEvent(new TorrentStartedEvent(torrent));
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent
            {
                Torrent = torrent,
                OldStatus = oldStatus,
                NewStatus = torrent.Status,
            });
        }

        if (changed)
        {
            _torrentRepository.Update(torrent);
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
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
                        var blockMethod = _blocklistService.GetType().GetMethod("AddBlockedIp", new[] { typeof(string) })
                                          ?? _blocklistService.GetType().GetMethod("BlockIp", new[] { typeof(string) });
                        if (blockMethod != null)
                        {
                            var ret = blockMethod.Invoke(_blocklistService, new object[] { ip });
                            if (ret is Task task)
                            {
                                task.ContinueWith(
                                    t =>
                                    {
                                        if (t.IsFaulted && t.Exception != null)
                                        {
                                            _logger.Error(t.Exception.GetBaseException(), "Failed to add peer {0} to blocklist", ip);
                                        }
                                    },
                                    TaskContinuationOptions.OnlyOnFaulted);
                            }
                        }
                        else
                        {
                            var task = _blocklistService.LoadRulesAsync(new[] { ip });
                            if (task != null)
                            {
                                task.ContinueWith(
                                    t =>
                                    {
                                        if (t.IsFaulted && t.Exception != null)
                                        {
                                            _logger.Error(t.Exception.GetBaseException(), "Failed to load peer rule {0} into blocklist", ip);
                                        }
                                    },
                                    TaskContinuationOptions.OnlyOnFaulted);
                            }
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
                        var disconnectMethod = _downloadEngine.GetType().GetMethod("DisconnectPeerAsync", new[] { typeof(int), typeof(string) })
                                               ?? _downloadEngine.GetType().GetMethod("DisconnectPeer", new[] { typeof(int), typeof(string) })
                                               ?? _downloadEngine.GetType().GetMethod("BanPeerAsync", new[] { typeof(int), typeof(string) });
                        if (disconnectMethod != null)
                        {
                            var ret = disconnectMethod.Invoke(_downloadEngine, new object[] { torrent.Id, ip });
                            if (ret is Task task)
                            {
                                task.ContinueWith(
                                    t =>
                                    {
                                        if (t.IsFaulted && t.Exception != null)
                                        {
                                            _logger.Error(t.Exception.GetBaseException(), "Failed to disconnect peer {0} on torrent {1}", ip, torrent.Id);
                                        }
                                    },
                                    TaskContinuationOptions.OnlyOnFaulted);
                            }
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
                                TaskContinuationOptions.OnlyOnFaulted);
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
                                TaskContinuationOptions.OnlyOnFaulted);
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
                            TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to boost tracker for torrent {0}", torrent.Name);
            }
        }

        // 3. Extract Archive
        if (result.ShouldExtractArchive && _extractorService != null)
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
                        TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract archive for torrent {0}", torrent.Name);
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
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }

        return string.Equals(fileName, trimmed, StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("." + trimmed.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
    }
}
