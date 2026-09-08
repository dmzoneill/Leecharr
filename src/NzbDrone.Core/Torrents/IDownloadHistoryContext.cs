// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public interface IDownloadHistoryContext
{
    ITrackerEntryRepository TrackerEntryRepository { get; }

    ISafeHttpClientService SafeHttpClientService { get; }

    ICategoryService CategoryService { get; }

    IStoragePathService StoragePathService { get; }

    ITorrentFileParser TorrentFileParser { get; }

    ITorrentFileRepository FileRepository { get; }

    IConfigService ConfigService { get; }

    IAppFolderInfo AppFolderInfo { get; }

    ITorrentMediaMetadataRepository MediaMetadataRepository { get; }
}

public class DownloadHistoryContext : IDownloadHistoryContext
{
    public DownloadHistoryContext(
        ITrackerEntryRepository trackerEntryRepository = null,
        ISafeHttpClientService safeHttpClientService = null,
        ICategoryService categoryService = null,
        IStoragePathService storagePathService = null,
        ITorrentFileParser torrentFileParser = null,
        ITorrentFileRepository fileRepository = null,
        IConfigService configService = null,
        IAppFolderInfo appFolderInfo = null,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null)
    {
        this.TrackerEntryRepository = trackerEntryRepository;
        this.SafeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.CategoryService = categoryService;
        this.StoragePathService = storagePathService;
        this.TorrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.FileRepository = fileRepository;
        this.ConfigService = configService;
        this.AppFolderInfo = appFolderInfo;
        this.MediaMetadataRepository = mediaMetadataRepository;
    }

    public ITrackerEntryRepository TrackerEntryRepository { get; }

    public ISafeHttpClientService SafeHttpClientService { get; }

    public ICategoryService CategoryService { get; }

    public IStoragePathService StoragePathService { get; }

    public ITorrentFileParser TorrentFileParser { get; }

    public ITorrentFileRepository FileRepository { get; }

    public IConfigService ConfigService { get; }

    public IAppFolderInfo AppFolderInfo { get; }

    public ITorrentMediaMetadataRepository MediaMetadataRepository { get; }
}
