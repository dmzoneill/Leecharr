// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net.Sockets;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Binding;

public interface INetworkBindingProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Version { get; }

    string Description { get; }

    bool IsAvailable { get; }

    NetworkBindingCapabilities Capabilities { get; }

    Task<NetworkBindingHealthCheckResult> ProbeHealthAsync();

    void BindSocket(Socket socket, string interfaceName);

    bool IsInterfaceUp(string interfaceName);
}
