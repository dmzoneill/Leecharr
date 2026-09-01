using System.Net;

namespace NzbDrone.Core.Authentication;

public interface ITrustedNetworkService
{
    bool IsLocalOrPrivateNetwork(IPAddress remoteIp);
    bool IsTrustedProxy(IPAddress remoteIp, string configuredCidrs);
}
