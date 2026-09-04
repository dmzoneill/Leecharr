// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Search;

public class QBittorrentSearchResultItem
{
    public string DescrLink { get; set; }

    public string FileName { get; set; }

    public long FileSize { get; set; }

    public string FileUrl { get; set; }

    public int NbLeechers { get; set; }

    public int NbSeeders { get; set; }

    public string SiteUrl { get; set; }
}

public class QBittorrentSearchStatus
{
    public int Id { get; set; }

    public string Status { get; set; }

    public int Total { get; set; }
}

public class QBittorrentSearchResultsResponse
{
    public List<QBittorrentSearchResultItem> Results { get; set; } = new();

    public string Status { get; set; }

    public int Total { get; set; }
}

public interface IQBittorrentSearchService
{
    int StartSearch(string pattern, string plugins = null, string category = null);

    bool StopSearch(int id);

    bool DeleteSearch(int id);

    QBittorrentSearchStatus GetStatus(int id);

    List<QBittorrentSearchStatus> GetAllStatuses();

    QBittorrentSearchResultsResponse GetResults(int id, int limit = 0, int offset = 0);

    List<object> GetPlugins();

    List<string> GetCategories();

    int PruneExpiredJobs();
}
