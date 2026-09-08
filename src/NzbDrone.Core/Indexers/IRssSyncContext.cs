// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.Http;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers;

public interface IRssSyncContext
{
    ITorrentFileParser TorrentFileParser { get; }

    HttpClient HttpClient { get; }

    ISafeHttpClientService SafeHttpClientService { get; }

    IDownloadHistoryService DownloadHistoryService { get; }

    ICategoryService CategoryService { get; }
}

public class RssSyncContext : IRssSyncContext
{
    public RssSyncContext(
        ITorrentFileParser torrentFileParser = null,
        HttpClient httpClient = null,
        ISafeHttpClientService safeHttpClientService = null,
        IDownloadHistoryService downloadHistoryService = null,
        ICategoryService categoryService = null)
    {
        this.TorrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.HttpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        this.SafeHttpClientService = safeHttpClientService ?? (httpClient != null ? new SafeHttpClientService(httpClient) : new SafeHttpClientService());
        this.DownloadHistoryService = downloadHistoryService;
        this.CategoryService = categoryService;
    }

    public ITorrentFileParser TorrentFileParser { get; }

    public HttpClient HttpClient { get; }

    public ISafeHttpClientService SafeHttpClientService { get; }

    public IDownloadHistoryService DownloadHistoryService { get; }

    public ICategoryService CategoryService { get; }
}
