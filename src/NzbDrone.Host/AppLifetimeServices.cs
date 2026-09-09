// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.Authentication;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.SystemServices;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;
using NzbDrone.SignalR;

namespace NzbDrone.Host;

public class AppLifetimeServices : IAppLifetimeServices
{
    public AppLifetimeServices(
        IConfigService configService,
        IEventAggregator eventAggregator,
        IDownloadEngine downloadEngine,
        ITorrentRepository torrentRepository,
        IWatchFolderService watchFolderService,
        INetworkSecurityService networkSecurityService,
        IRssSyncService rssSyncService,
        IDynamicAuthSchemeManager dynamicAuthManager,
        ITorrentService torrentService = null,
        IBroadcastSignalRMessage signalRBroadcaster = null,
        IQueueManagerService queueManagerService = null,
        IPowerManagementService powerManagementService = null,
        IUdpTrackerService udpTrackerService = null,
        IAppFolderInfo appFolderInfo = null,
        ICategoryService categoryService = null,
        IProwlarrSyncService prowlarrSyncService = null,
        IManageCommandQueue commandQueueManager = null)
    {
        this.ConfigService = configService;
        this.EventAggregator = eventAggregator;
        this.DownloadEngine = downloadEngine;
        this.TorrentRepository = torrentRepository;
        this.WatchFolderService = watchFolderService;
        this.NetworkSecurityService = networkSecurityService;
        this.RssSyncService = rssSyncService;
        this.DynamicAuthManager = dynamicAuthManager;
        this.TorrentService = torrentService;
        this.SignalRBroadcaster = signalRBroadcaster;
        this.QueueManagerService = queueManagerService;
        this.PowerManagementService = powerManagementService ?? new PowerManagementService();
        this.UdpTrackerService = udpTrackerService;
        this.AppFolderInfo = appFolderInfo;
        this.CategoryService = categoryService;
        this.ProwlarrSyncService = prowlarrSyncService;
        this.CommandQueueManager = commandQueueManager;
    }

    public IConfigService ConfigService { get; }

    public IEventAggregator EventAggregator { get; }

    public IDownloadEngine DownloadEngine { get; }

    public ITorrentRepository TorrentRepository { get; }

    public IWatchFolderService WatchFolderService { get; }

    public INetworkSecurityService NetworkSecurityService { get; }

    public IRssSyncService RssSyncService { get; }

    public IDynamicAuthSchemeManager DynamicAuthManager { get; }

    public ITorrentService TorrentService { get; }

    public IBroadcastSignalRMessage SignalRBroadcaster { get; }

    public IQueueManagerService QueueManagerService { get; }

    public IPowerManagementService PowerManagementService { get; }

    public IUdpTrackerService UdpTrackerService { get; }

    public IAppFolderInfo AppFolderInfo { get; }

    public ICategoryService CategoryService { get; }

    public IProwlarrSyncService ProwlarrSyncService { get; }

    public IManageCommandQueue CommandQueueManager { get; }
}
