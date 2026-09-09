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

public interface IAppLifetimeServices
{
    IConfigService ConfigService { get; }

    IEventAggregator EventAggregator { get; }

    IDownloadEngine DownloadEngine { get; }

    ITorrentRepository TorrentRepository { get; }

    IWatchFolderService WatchFolderService { get; }

    INetworkSecurityService NetworkSecurityService { get; }

    IRssSyncService RssSyncService { get; }

    IDynamicAuthSchemeManager DynamicAuthManager { get; }

    ITorrentService TorrentService { get; }

    IBroadcastSignalRMessage SignalRBroadcaster { get; }

    IQueueManagerService QueueManagerService { get; }

    IPowerManagementService PowerManagementService { get; }

    IUdpTrackerService UdpTrackerService { get; }

    IAppFolderInfo AppFolderInfo { get; }

    ICategoryService CategoryService { get; }

    IProwlarrSyncService ProwlarrSyncService { get; }

    IManageCommandQueue CommandQueueManager { get; }
}
