// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class ProtocolsConfigResource : RestResource
{
    public bool ExtensionUtMetadata { get; set; }

    public bool ExtensionUtPex { get; set; }

    public bool ExtensionLtDontHave { get; set; }

    public bool ExtensionFastExtension { get; set; }

    public bool UtpEnabled { get; set; }

    public bool TcpFallback { get; set; }

    public int TransportConnectionTimeoutSeconds { get; set; }

    public int PexInterval { get; set; }

    public int PexMaxPeersPerMessage { get; set; }

    public bool MultiTrackerEnabled { get; set; }

    public bool MultiTrackerFailoverEnabled { get; set; }

    public bool AnnounceToAllTiers { get; set; }

    public bool AnnounceToAllInTier { get; set; }

    public int FailoverMaxConsecutiveFailures { get; set; }

    public int FailoverBackoffBaseSeconds { get; set; }

    public int FailoverMaxBackoffSeconds { get; set; }

    public int DhtRoutingTableSize { get; set; }

    public int DhtAnnouncementInterval { get; set; }

    public int DhtBootstrapTimeout { get; set; }

    public int DhtQueryTimeout { get; set; }

    public int DhtMaxNodes { get; set; }

    public int DhtBucketSize { get; set; }

    public int DhtConcurrentQueries { get; set; }

    public bool DhtAutoBootstrap { get; set; }

    public bool DhtRateLimitEnabled { get; set; }

    public int DhtMaxQueriesPerSecond { get; set; }
}

public static class ProtocolsConfigResourceMapper
{
    public static ProtocolsConfigResource ToResource(IConfigService model)
    {
        return new ProtocolsConfigResource
        {
            ExtensionUtMetadata = model.ExtensionUtMetadata,
            ExtensionUtPex = model.ExtensionUtPex,
            ExtensionLtDontHave = model.ExtensionLtDontHave,
            ExtensionFastExtension = model.ExtensionFastExtension,
            UtpEnabled = model.UtpEnabled,
            TcpFallback = model.TcpFallback,
            TransportConnectionTimeoutSeconds = model.TransportConnectionTimeoutSeconds,
            PexInterval = model.PexInterval,
            PexMaxPeersPerMessage = model.PexMaxPeersPerMessage,
            MultiTrackerEnabled = model.MultiTrackerEnabled,
            MultiTrackerFailoverEnabled = model.MultiTrackerFailoverEnabled,
            AnnounceToAllTiers = model.AnnounceToAllTiers,
            AnnounceToAllInTier = model.AnnounceToAllInTier,
            FailoverMaxConsecutiveFailures = model.FailoverMaxConsecutiveFailures,
            FailoverBackoffBaseSeconds = model.FailoverBackoffBaseSeconds,
            FailoverMaxBackoffSeconds = model.FailoverMaxBackoffSeconds,
            DhtRoutingTableSize = model.DhtRoutingTableSize,
            DhtAnnouncementInterval = model.DhtAnnouncementInterval,
            DhtBootstrapTimeout = model.DhtBootstrapTimeout,
            DhtQueryTimeout = model.DhtQueryTimeout,
            DhtMaxNodes = model.DhtMaxNodes,
            DhtBucketSize = model.DhtBucketSize,
            DhtConcurrentQueries = model.DhtConcurrentQueries,
            DhtAutoBootstrap = model.DhtAutoBootstrap,
            DhtRateLimitEnabled = model.DhtRateLimitEnabled,
            DhtMaxQueriesPerSecond = model.DhtMaxQueriesPerSecond,
        };
    }
}
