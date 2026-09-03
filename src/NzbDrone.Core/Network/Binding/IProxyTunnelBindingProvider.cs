// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Binding;

public interface IProxyTunnelBindingProvider : INetworkBindingProvider
{
    Task<Socket> ConnectTunnelAsync(string targetHost, int targetPort, CancellationToken cancellationToken = default);

    Socket ConnectTunnel(string targetHost, int targetPort);
}
