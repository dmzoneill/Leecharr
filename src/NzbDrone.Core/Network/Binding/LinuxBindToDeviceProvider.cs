using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.Binding;

public class LinuxBindToDeviceProvider : INetworkBindingProvider
{
    private const int SoBindToDevice = 25;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "LinuxBindToDevice";
    public string DisplayName => "Linux Kernel Device Binding (SO_BINDTODEVICE)";
    public string Version => "1.0.0";
    public string Description => "Kernel-level socket binding directly locking network traffic to specific network interfaces (SO_BINDTODEVICE socket option 25).";
    public bool IsAvailable => OsInfo.IsLinux || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public NetworkBindingCapabilities Capabilities => new()
    {
        SupportsInterfaceBinding = true,
        SupportsSoBindToDevice = true,
        SupportsSocks5Proxy = false,
        SupportsTorOnion = false,
        SupportsVpnKillSwitch = true,
        SupportsAnonymousRouting = false
    };

    public Task<NetworkBindingHealthCheckResult> ProbeHealthAsync()
    {
        if (!IsAvailable)
        {
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "SO_BINDTODEVICE is only supported on Linux operating systems.",
                Warnings = { "Current operating system does not support raw SO_BINDTODEVICE kernel socket option." }
            });
        }

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"Linux SO_BINDTODEVICE kernel socket option available ({interfaces.Length} interfaces enumerated)."
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Failed to probe Linux network interfaces: {ex.Message}",
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

        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("SO_BINDTODEVICE is only supported on Linux platforms.");
        }

        try
        {
            var ifaceBytes = Encoding.ASCII.GetBytes(interfaceName + "\0");
            socket.SetRawSocketOption((int)SocketOptionLevel.Socket, SoBindToDevice, ifaceBytes);
            _logger.Debug("Applied SO_BINDTODEVICE kernel lock to socket for interface '{0}'", interfaceName);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to apply SO_BINDTODEVICE on interface '{0}', falling back to standard bind", interfaceName);
            throw;
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
        catch
        {
            return false;
        }
    }
}
