// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;

namespace NzbDrone.Core.Telemetry;

public interface ISubsystemManagerRegistry
{
    ITorrentEngineManager TorrentEngineManager { get; }

    IArchiveExtractorManager ExtractorManager { get; }

    IMediaInspectorManager MediaInspectorManager { get; }

    IGeoIpManager GeoIpManager { get; }

    IBlocklistManager BlocklistManager { get; }

    INetworkBindingManager NetworkBindingManager { get; }

    IMediaMetadataManager MediaMetadataManager { get; }

    IHttpTransportManager HttpTransportManager { get; }

    IAiManager AiManager { get; }
}

public class SubsystemManagerRegistry : ISubsystemManagerRegistry
{
    public SubsystemManagerRegistry(
        ITorrentEngineManager torrentEngineManager = null,
        IArchiveExtractorManager extractorManager = null,
        IMediaInspectorManager mediaInspectorManager = null,
        IGeoIpManager geoIpManager = null,
        IBlocklistManager blocklistManager = null,
        INetworkBindingManager networkBindingManager = null,
        IMediaMetadataManager mediaMetadataManager = null,
        IHttpTransportManager httpTransportManager = null,
        IAiManager aiManager = null)
    {
        this.TorrentEngineManager = torrentEngineManager;
        this.ExtractorManager = extractorManager;
        this.MediaInspectorManager = mediaInspectorManager;
        this.GeoIpManager = geoIpManager;
        this.BlocklistManager = blocklistManager;
        this.NetworkBindingManager = networkBindingManager;
        this.MediaMetadataManager = mediaMetadataManager;
        this.HttpTransportManager = httpTransportManager;
        this.AiManager = aiManager;
    }

    public ITorrentEngineManager TorrentEngineManager { get; }

    public IArchiveExtractorManager ExtractorManager { get; }

    public IMediaInspectorManager MediaInspectorManager { get; }

    public IGeoIpManager GeoIpManager { get; }

    public IBlocklistManager BlocklistManager { get; }

    public INetworkBindingManager NetworkBindingManager { get; }

    public IMediaMetadataManager MediaMetadataManager { get; }

    public IHttpTransportManager HttpTransportManager { get; }

    public IAiManager AiManager { get; }
}
