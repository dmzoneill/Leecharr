// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Tracker;

public class EmbeddedTrackerHttpProcessor : IEmbeddedTrackerHttpProcessor
{
    private readonly IEmbeddedTrackerService trackerService;
    private readonly IConfigService configService;
    private readonly ITrackerClientRateLimiter rateLimiter;
    private readonly ITrustedNetworkService trustedNetworkService;

    public EmbeddedTrackerHttpProcessor(
        IEmbeddedTrackerService trackerService,
        IConfigService configService = null,
        ITrackerClientRateLimiter rateLimiter = null,
        ITrustedNetworkService trustedNetworkService = null)
    {
        this.trackerService = trackerService;
        this.configService = configService;
        this.rateLimiter = rateLimiter ?? new TrackerClientRateLimiter();
        this.trustedNetworkService = trustedNetworkService ?? new TrustedNetworkService();
    }

    public EmbeddedTrackerHttpResponse ProcessAnnounce(EmbeddedTrackerHttpRequest request)
    {
        if (!EmbeddedTrackerController.IsHttpTrackerRequestOnBindAddress(this.configService, request?.LocalIp))
        {
            return EmbeddedTrackerHttpResponse.NotFound();
        }

        var remoteIp = EmbeddedTrackerController.ResolveRemoteIp(request, this.trustedNetworkService, this.configService);
        var rateLimited = EmbeddedTrackerController.RateLimitResponseIfNeeded(remoteIp, this.configService, this.rateLimiter);
        if (rateLimited != null)
        {
            return rateLimited;
        }

        var announceRequest = EmbeddedTrackerController.ParseAnnounceQuery(request?.RawQuery, remoteIp);
        var responseBytes = this.trackerService.ProcessAnnounce(announceRequest);
        return EmbeddedTrackerHttpResponse.Plain(responseBytes);
    }

    public EmbeddedTrackerHttpResponse ProcessScrape(EmbeddedTrackerHttpRequest request)
    {
        if (!EmbeddedTrackerController.IsHttpTrackerRequestOnBindAddress(this.configService, request?.LocalIp))
        {
            return EmbeddedTrackerHttpResponse.NotFound();
        }

        var remoteIp = EmbeddedTrackerController.ResolveRemoteIp(request, this.trustedNetworkService, this.configService);
        var rateLimited = EmbeddedTrackerController.RateLimitResponseIfNeeded(remoteIp, this.configService, this.rateLimiter);
        if (rateLimited != null)
        {
            return rateLimited;
        }

        var hashes = EmbeddedTrackerController.ParseScrapeQuery(request?.RawQuery);
        var responseBytes = this.trackerService.ProcessScrape(hashes);
        return EmbeddedTrackerHttpResponse.Plain(responseBytes);
    }
}
