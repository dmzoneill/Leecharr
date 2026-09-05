// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

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
        SupportsAnonymousRouting = false,
    };

    public Task<NetworkBindingHealthCheckResult> ProbeHealthAsync()
    {
        if (!this.IsAvailable)
        {
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "SO_BINDTODEVICE is only supported on Linux operating systems.",
                Warnings = { "Current operating system does not support raw SO_BINDTODEVICE kernel socket option." },
            });
        }

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Test if process has CAP_NET_RAW capability for SO_BINDTODEVICE (option 25)
            try
            {
                using var testSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var loBytes = Encoding.ASCII.GetBytes("lo\0");
                testSocket.SetRawSocketOption((int)SocketOptionLevel.Socket, SoBindToDevice, loBytes);
            }
            catch (SocketException sex) when (sex.SocketErrorCode == SocketError.AccessDenied || sex.NativeErrorCode == 1)
            {
                return Task.FromResult(new NetworkBindingHealthCheckResult
                {
                    IsHealthy = false,
                    StatusMessage = "Linux SO_BINDTODEVICE requires CAP_NET_RAW permission (EPERM / Access Denied).",
                    Warnings = { "Process lacks CAP_NET_RAW capability required for SO_BINDTODEVICE kernel binding. Grant CAP_NET_RAW or run as root/privileged container." },
                });
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Capability probe non-fatal exception during SO_BINDTODEVICE test");
            }

            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"Linux SO_BINDTODEVICE kernel socket option available ({interfaces.Length} interfaces enumerated).",
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Failed to probe Linux network interfaces: {ex.Message}",
                Warnings = { ex.ToString() },
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

        if (!this.IsAvailable)
        {
            throw new PlatformNotSupportedException("SO_BINDTODEVICE is only supported on Linux platforms.");
        }

        try
        {
            var ifaceBytes = Encoding.ASCII.GetBytes(interfaceName + "\0");
            socket.SetRawSocketOption((int)SocketOptionLevel.Socket, SoBindToDevice, ifaceBytes);
            this.logger.Debug("Applied SO_BINDTODEVICE kernel lock to socket for interface '{0}'", interfaceName);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to apply SO_BINDTODEVICE on interface '{0}'", interfaceName);
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
