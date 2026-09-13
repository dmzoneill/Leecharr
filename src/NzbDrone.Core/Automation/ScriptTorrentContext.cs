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

    public void setRatioLimit(double ratio)
    {
        _result.NewRatioLimit = ratio;
    }

    public void setSeedingTimeLimit(int minutes)
    {
        _result.NewSeedingTimeLimitMinutes = minutes;
    }

    public void setPriority(int priority)
    {
        _result.NewPriority = priority;
    }

    public void setPriority(string priority)
    {
        if (int.TryParse(priority, out var pInt))
        {
            _result.NewPriority = pInt;
        }
        else if (string.Equals(priority, "high", System.StringComparison.OrdinalIgnoreCase))
        {
            _result.NewPriority = 2;
        }
        else if (string.Equals(priority, "low", System.StringComparison.OrdinalIgnoreCase))
        {
            _result.NewPriority = 0;
        }
        else if (string.Equals(priority, "donotdownload", System.StringComparison.OrdinalIgnoreCase) || string.Equals(priority, "off", System.StringComparison.OrdinalIgnoreCase))
        {
            _result.NewPriority = -1;
        }
        else
        {
            _result.NewPriority = 1; // Normal
        }
    }

    public void setSequential(bool enabled = true)
    {
        _result.NewSequentialDownload = enabled;
    }

    public void setSequentialDownload(bool enabled = true)
    {
        _result.NewSequentialDownload = enabled;
    }

    public void setSuperSeeding(bool enabled = true)
    {
        _result.NewSuperSeeding = enabled;
    }

    public void moveFiles(string destination)
    {
        if (!string.IsNullOrWhiteSpace(destination))
        {
            _result.NewSavePath = destination.Trim();
        }
    }

    public void setSavePath(string destination)
    {
        moveFiles(destination);
    }

    public void addTracker(string trackerUrl)
    {
        if (!string.IsNullOrWhiteSpace(trackerUrl) && !_result.TrackersToAdd.Contains(trackerUrl.Trim()))
        {
            _result.TrackersToAdd.Add(trackerUrl.Trim());
        }
    }

    public void removeTracker(string pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern) && !_result.TrackersToRemove.Contains(pattern.Trim()))
        {
            _result.TrackersToRemove.Add(pattern.Trim());
        }
    }

    public void boostTracker()
    {
        _result.ShouldBoostTracker = true;
    }

    public void banPeer(string ip)
    {
        if (!string.IsNullOrWhiteSpace(ip) && !_result.PeersToBan.Contains(ip.Trim()))
        {
            _result.PeersToBan.Add(ip.Trim());
        }
    }

    public void extractArchive(string? destination = null, bool deleteArchive = false)
    {
        _result.ShouldExtractArchive = true;
        _result.ExtractDestination = destination;
        _result.DeleteArchiveOnExtract = deleteArchive;
    }

    public void cleanUnwantedFiles(params string[] patterns)
    {
        if (patterns != null)
        {
            foreach (var p in patterns)
            {
                if (!string.IsNullOrWhiteSpace(p) && !_result.CleanFilePatterns.Contains(p.Trim()))
                {
                    _result.CleanFilePatterns.Add(p.Trim());
                }
            }
        }
    }

    public void cleanFiles(string patterns)
    {
        if (!string.IsNullOrWhiteSpace(patterns))
        {
            var parts = patterns.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            cleanUnwantedFiles(parts);
        }
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
