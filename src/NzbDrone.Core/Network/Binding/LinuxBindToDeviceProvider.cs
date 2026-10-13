// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using System.Net;
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
    private const int SolSocket = 1;
    private const int SoBindToDevice = 25;
    private const int Ifnamesiz = 16;
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

    protected virtual bool IsBindToDevicePlatform => this.IsAvailable;

    protected virtual void ApplySoBindToDevice(Socket socket, byte[] option)
    {
        socket.SetRawSocketOption(SolSocket, SoBindToDevice, option);
    }

    public Task<NetworkBindingHealthCheckResult> ProbeHealthAsync()
    {
        if (!this.IsBindToDevicePlatform)
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
                this.ApplySoBindToDevice(testSocket, loBytes);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                return Task.FromResult(new NetworkBindingHealthCheckResult
                {
                    IsHealthy = false,
                    StatusMessage = "Linux SO_BINDTODEVICE requires CAP_NET_RAW permission (EPERM / Access Denied).",
                    Warnings =
                    {
                        "Process lacks CAP_NET_RAW capability required for SO_BINDTODEVICE kernel binding. Grant CAP_NET_RAW or run as root/privileged container.",
                        ex.Message,
                    },
                });
            }
            catch (SocketException sex)
            {
                return Task.FromResult(new NetworkBindingHealthCheckResult
                {
                    IsHealthy = false,
                    StatusMessage = $"Linux SO_BINDTODEVICE probe failed: {sex.Message}",
                    Warnings = { sex.ToString() },
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new NetworkBindingHealthCheckResult
                {
                    IsHealthy = false,
                    StatusMessage = $"Linux SO_BINDTODEVICE probe failed: {ex.Message}",
                    Warnings = { ex.ToString() },
                });
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

    public void BindSocket(Socket socket, string interfaceName, int localPort = 0)
    {
        ArgumentNullException.ThrowIfNull(socket);

        if (string.IsNullOrWhiteSpace(interfaceName) ||
            string.Equals(interfaceName, "Any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(interfaceName, "all", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Encoding.ASCII.GetByteCount(interfaceName) >= Ifnamesiz)
        {
            throw new ArgumentException($"Interface name '{interfaceName}' exceeds maximum Linux interface name length (IFNAMSIZ = {Ifnamesiz} bytes).", nameof(interfaceName));
        }

        if (!this.IsBindToDevicePlatform)
        {
            throw new PlatformNotSupportedException("SO_BINDTODEVICE is only supported on Linux platforms.");
        }

        var kernelInterfaceName = ResolveKernelInterfaceName(interfaceName);

        try
        {
            var ifaceBytes = Encoding.ASCII.GetBytes(kernelInterfaceName + "\0");
            this.ApplySoBindToDevice(socket, ifaceBytes);
            this.logger.Debug(
                "Applied SO_BINDTODEVICE kernel lock to socket for interface '{0}' (resolved from '{1}')",
                kernelInterfaceName,
                interfaceName);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            if (TryBindToInterfaceAddress(socket, kernelInterfaceName, localPort, out var boundIp))
            {
                this.logger.Warn(
                    ex,
                    "SO_BINDTODEVICE was denied for interface '{0}'. Bound the socket to {1} instead.",
                    kernelInterfaceName,
                    boundIp);
                return;
            }

            this.logger.Warn(
                ex,
                "SO_BINDTODEVICE was denied for interface '{0}' and no interface address was available.",
                kernelInterfaceName);
            throw;
        }
        catch (Exception ex)
        {
            this.logger.Warn(
                ex,
                "Failed to apply SO_BINDTODEVICE on interface '{0}' (resolved from '{1}')",
                kernelInterfaceName,
                interfaceName);
            throw;
        }
    }

    private static bool IsAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        if (ex is SocketException socketException)
        {
            return socketException.SocketErrorCode == SocketError.AccessDenied
                || socketException.NativeErrorCode == 1
                || socketException.NativeErrorCode == 13;
        }

        return false;
    }

    private static bool TryBindToInterfaceAddress(Socket socket, string interfaceName, int localPort, out IPAddress boundIp)
    {
        boundIp = null;
        if (socket.IsBound)
        {
            return false;
        }

        var ip = FindInterfaceAddress(interfaceName, socket.AddressFamily);
        if (ip == null)
        {
            return false;
        }

        socket.Bind(new IPEndPoint(ip, localPort));
        boundIp = ip;
        return true;
    }

    public bool IsInterfaceUp(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) ||
            string.Equals(interfaceName, "Any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(interfaceName, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Encoding.ASCII.GetByteCount(interfaceName) >= Ifnamesiz)
        {
            return false;
        }

        try
        {
            var nic = FindNetworkInterface(interfaceName);

            if (nic == null || nic.OperationalStatus != OperationalStatus.Up)
            {
                return false;
            }

            var unicast = nic.GetIPProperties()?.UnicastAddresses;
            return unicast != null && unicast.Any(a =>
                !IPAddress.IsLoopback(a.Address) &&
                !a.Address.Equals(IPAddress.Any) &&
                !a.Address.Equals(IPAddress.None) &&
                !a.Address.Equals(IPAddress.IPv6Any) &&
                !a.Address.Equals(IPAddress.IPv6None));
        }
        catch
        {
            return false;
        }
    }

    private static NetworkInterface FindNetworkInterface(string interfaceName)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));
    }

    private static IPAddress FindInterfaceAddress(string interfaceName, AddressFamily addressFamily)
    {
        var nic = FindNetworkInterface(interfaceName);
        return nic?.GetIPProperties()?.UnicastAddresses?
            .Select(address => address.Address)
            .FirstOrDefault(address => address.AddressFamily == addressFamily);
    }

    private static string ResolveKernelInterfaceName(string interfaceName)
    {
        return FindNetworkInterface(interfaceName)?.Name ?? interfaceName;
    }
}
