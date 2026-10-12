// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Http.Transport;

public interface IHttpOutboundProxyRefreshable
{
    void RefreshOutboundProxy();
}
