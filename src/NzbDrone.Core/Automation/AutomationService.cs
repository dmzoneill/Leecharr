#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

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
        IWebhookDispatcher? webhookDispatcher = null)
    {
        _scriptRepository = scriptRepository;
        _torrentRepository = torrentRepository;
        _tagRepository = tagRepository;
        _eventAggregator = eventAggregator;
        _commandQueue = commandQueue;
        _customScriptService = customScriptService;
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
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
                    : activeNotifications.Where(n => string.Equals(n.Implementation, notif.Provider, StringComparison.OrdinalIgnoreCase) ||
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
        return runner.Execute(script, torrent, torrentTags, customInputs);
    }

    private void ApplyTorrentMutations(Torrent torrent, AutomationExecutionResult result)
    {
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

        if (result.ShouldPause && torrent.Status != TorrentStatus.Paused)
        {
            torrent.Status = TorrentStatus.Paused;
            changed = true;
        }
        else if (result.ShouldResume && torrent.Status == TorrentStatus.Paused)
        {
            torrent.Status = (torrent.Progress >= 1.0f || torrent.Progress >= 0.999f || torrent.DateCompleted != null)
                ? TorrentStatus.Seeding
                : TorrentStatus.Downloading;
            changed = true;
        }

        if (changed)
        {
            _torrentRepository.Update(torrent);
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        }
    }
}
