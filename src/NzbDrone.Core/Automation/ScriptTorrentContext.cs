#nullable enable
#pragma warning disable SA1300
using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

public class ScriptTorrentContext
{
    private readonly Torrent _torrent;
    private readonly AutomationExecutionResult _result;
    private readonly List<string> _tagNames;

    public ScriptTorrentContext(Torrent torrent, AutomationExecutionResult result, List<string>? tagNames = null)
    {
        _torrent = torrent ?? throw new System.ArgumentNullException(nameof(torrent));
        _result = result ?? throw new System.ArgumentNullException(nameof(result));
        _tagNames = tagNames ?? new List<string>();
    }

    public int id => _torrent.Id;

    public string name => _torrent.Name ?? string.Empty;

    public string infoHash => _torrent.InfoHash ?? string.Empty;

    public long size => _torrent.TotalSize;

    public long totalSize => _torrent.TotalSize;

    public double ratio => _torrent.Ratio;

    public long uploaded => _torrent.Uploaded;

    public long downloaded => _torrent.Downloaded;

    public string category => _torrent.Category ?? string.Empty;

    public string savePath => _torrent.SavePath ?? string.Empty;

    public string tracker => _torrent.TrackerUrl ?? string.Empty;

    public string trackerUrl => _torrent.TrackerUrl ?? string.Empty;

    public string status => _torrent.Status.ToString();

    public double progress => _torrent.Progress;

    public long uploadSpeed => _torrent.UploadSpeed;

    public long downloadSpeed => _torrent.DownloadSpeed;

    public int seeders => _torrent.Seeders;

    public int leechers => _torrent.Leechers;

    public string comment => _torrent.Comment ?? string.Empty;

    public bool isPrivate => _torrent.IsPrivate;

    public List<int> tagIds => _torrent.TagIds != null ? new List<int>(_torrent.TagIds) : new List<int>();

    public List<string> tags => new(_tagNames);

    public void addTag(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag) && !_result.TagsToAdd.Contains(tag))
        {
            _result.TagsToAdd.Add(tag.Trim());
        }
    }

    public void removeTag(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag) && !_result.TagsToRemove.Contains(tag))
        {
            _result.TagsToRemove.Add(tag.Trim());
        }
    }

    public void setCategory(string newCategory)
    {
        _result.NewCategory = newCategory ?? string.Empty;
    }

    public void pause()
    {
        _result.ShouldPause = true;
        _result.ShouldResume = false;
    }

    public void resume()
    {
        _result.ShouldResume = true;
        _result.ShouldPause = false;
    }

    public void remove(bool deleteData = false)
    {
        _result.ShouldRemove = true;
        _result.DeleteDataOnRemove = deleteData;
    }

    public void setUploadLimit(int limitKbps)
    {
        _result.NewUploadLimitKbps = limitKbps;
    }

    public void setDownloadLimit(int limitKbps)
    {
        _result.NewDownloadLimitKbps = limitKbps;
    }

    public void recheck()
    {
        _result.ShouldRecheck = true;
    }

    public void reannounce()
    {
        _result.ShouldReannounce = true;
    }
}
