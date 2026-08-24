using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.Binding;

public class ManagedSocketBindingProvider : INetworkBindingProvider
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "ManagedSocket";
    public string DisplayName => "Managed Socket Binding (.NET Standard)";
    public string Version => "1.0.0";
    public string Description => "Cross-platform managed socket binding using .NET Socket.Bind() with IP endpoint resolution.";
    public bool IsAvailable => true;

    public NetworkBindingCapabilities Capabilities => new()
    {
        SupportsInterfaceBinding = true,
        SupportsSoBindToDevice = false,
        SupportsSocks5Proxy = false,
        SupportsTorOnion = false,
        SupportsVpnKillSwitch = true,
        SupportsAnonymousRouting = false
    };

    public Task<NetworkBindingHealthCheckResult> ProbeHealthAsync()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var upCount = interfaces.Count(i => i.OperationalStatus == OperationalStatus.Up);
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"Managed socket provider operational ({upCount}/{interfaces.Length} interfaces active)."
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Interface probe failed: {ex.Message}",
                Warnings = { ex.ToString() }
            });
        }
    }

    public void BindSocket(Socket socket, string interfaceName)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return;
        }

        var ip = GetInterfaceIp(interfaceName, socket.AddressFamily);
        if (ip != null)
        {
            socket.Bind(new IPEndPoint(ip, 0));
            _logger.Debug("Bound socket to interface '{0}' ({1})", interfaceName, ip);
        }
        else
        {
            _logger.Warn("Could not find matching IP address for interface '{0}' and family {1}", interfaceName, socket.AddressFamily);
        }
    }

    public bool IsInterfaceUp(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return true;
        }

        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase));

            return nic != null && nic.OperationalStatus == OperationalStatus.Up;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to check interface status for {0}", interfaceName);
            return false;
        }
    }

    private static IPAddress GetInterfaceIp(string interfaceName, AddressFamily addressFamily)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null)
            {
                return null;
            }

            var props = nic.GetIPProperties();
            var unicast = props.UnicastAddresses
                .FirstOrDefault(u => u.Address.AddressFamily == addressFamily);

            return unicast?.Address;
        }
        catch
        {
            return null;
        }
    }
}
