using System.Net.Sockets;

namespace NzbDrone.Core.Network.Binding;

public interface INetworkBindingService
{
    INetworkBindingProvider ActiveProvider { get; }
    string ActiveProviderId { get; }
    void BindSocket(Socket socket, string interfaceName);
    bool IsInterfaceUp(string interfaceName);
    bool CheckVpnKillSwitch(string interfaceName);
}
