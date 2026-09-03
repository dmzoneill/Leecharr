// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.PortMapping;

public enum NatPmpProtocol
{
    Udp = 1,
    Tcp = 2,
}

public class ActivePortMapping
{
    public int InternalPort { get; set; }

    public NatPmpProtocol Protocol { get; set; }

    public int ExternalPort { get; set; }

    public int LifetimeSeconds { get; set; }

    public IPAddress GatewayAddress { get; set; }

    public uint LastEpoch { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime NextRenewalUtc { get; set; }
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

public interface INatPmpPortMapperService : IDisposable
{
    IReadOnlyCollection<ActivePortMapping> ActiveMappings { get; }

    Task<IPAddress> GetExternalIpAddressAsync(IPAddress gateway = null, CancellationToken cancellationToken = default);

    Task<NatPmpMappingResult> MapPortAsync(int internalPort, NatPmpProtocol protocol, int suggestedExternalPort = 0, int lifetimeSeconds = 3600, IPAddress gateway = null, CancellationToken cancellationToken = default);

    Task<bool> UnmapPortAsync(int internalPort, NatPmpProtocol protocol, IPAddress gateway = null, CancellationToken cancellationToken = default);

    Task RenewAllMappingsAsync(bool force = false, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
