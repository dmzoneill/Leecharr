#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

public class AutomationService : IAutomationService
{
    private readonly IAutomationScriptRepository _scriptRepository;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly JintScriptRunner _jintRunner;
    private readonly YamlScriptRunner _yamlRunner;

    public AutomationService(
        IAutomationScriptRepository scriptRepository,
        ITorrentRepository torrentRepository,
        ITagRepository tagRepository,
        IEventAggregator eventAggregator,
        IManageCommandQueue? commandQueue = null,
        IConfigFileProvider? configFileProvider = null)
    {
        _scriptRepository = scriptRepository;
        _torrentRepository = torrentRepository;
        _tagRepository = tagRepository;
        _eventAggregator = eventAggregator;
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
            torrent.Status = TorrentStatus.Seeding;
            changed = true;
        }

        if (changed)
        {
            _torrentRepository.Update(torrent);
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        }
    }
}
