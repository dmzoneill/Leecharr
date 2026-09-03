// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.PortMapping;

public enum NatPmpProtocol
{
    Udp = 1,
    Tcp = 2,
}

public class NatPmpMappingResult
{
    public bool Success { get; set; }

    public int InternalPort { get; set; }

    public int ExternalPort { get; set; }

    public int LifetimeSeconds { get; set; }

    public IPAddress GatewayAddress { get; set; }

    public string ErrorMessage { get; set; }
}

public interface INatPmpPortMapperService
{
    Task<IPAddress> GetExternalIpAddressAsync(IPAddress gateway = null, CancellationToken cancellationToken = default);

    Task<NatPmpMappingResult> MapPortAsync(int internalPort, NatPmpProtocol protocol, int suggestedExternalPort = 0, int lifetimeSeconds = 3600, IPAddress gateway = null, CancellationToken cancellationToken = default);

    Task<bool> UnmapPortAsync(int internalPort, NatPmpProtocol protocol, IPAddress gateway = null, CancellationToken cancellationToken = default);
}
