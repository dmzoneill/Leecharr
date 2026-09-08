// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public interface ITorrentServiceContext
{
    ICategoryService CategoryService { get; }

    IMediaEnrichmentService MediaEnrichmentService { get; }

    IConfigService ConfigService { get; }

    ITrackerEntryRepository TrackerEntryRepository { get; }

    IQueueManagerService QueueManagerService { get; }

    IStoragePathService StoragePathService { get; }

    IAppFolderInfo AppFolderInfo { get; }

    ITorrentLogService TorrentLogService { get; }

    ISpeedSchedulerService SpeedSchedulerService { get; }
}

public class TorrentServiceContext : ITorrentServiceContext
{
    public TorrentServiceContext(
        ICategoryService categoryService = null,
        IMediaEnrichmentService mediaEnrichmentService = null,
        IConfigService configService = null,
        ITrackerEntryRepository trackerEntryRepository = null,
        IQueueManagerService queueManagerService = null,
        IStoragePathService storagePathService = null,
        IAppFolderInfo appFolderInfo = null,
        ITorrentLogService torrentLogService = null,
        ISpeedSchedulerService speedSchedulerService = null)
    {
        this.CategoryService = categoryService;
        this.MediaEnrichmentService = mediaEnrichmentService;
        this.ConfigService = configService;
        this.TrackerEntryRepository = trackerEntryRepository;
        this.QueueManagerService = queueManagerService;
        this.StoragePathService = storagePathService;
        this.AppFolderInfo = appFolderInfo;
        this.TorrentLogService = torrentLogService;
        this.SpeedSchedulerService = speedSchedulerService;
    }

    public ICategoryService CategoryService { get; }

    public IMediaEnrichmentService MediaEnrichmentService { get; }

    public IConfigService ConfigService { get; }

    public ITrackerEntryRepository TrackerEntryRepository { get; }

    public IQueueManagerService QueueManagerService { get; }

    public IStoragePathService StoragePathService { get; }

    public IAppFolderInfo AppFolderInfo { get; }

    public ITorrentLogService TorrentLogService { get; }

    public ISpeedSchedulerService SpeedSchedulerService { get; }
}
