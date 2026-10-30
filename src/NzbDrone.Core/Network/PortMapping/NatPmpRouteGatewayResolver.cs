// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.PortMapping;

internal static class NatPmpRouteGatewayResolver
{
    private const int RtfUp = 0x1;
    private const int RtfGateway = 0x2;

    public static IPAddress TryGetGatewayFromRouteTable(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return null;
        }

        try
        {
            if (OsInfo.IsLinux && File.Exists("/proc/net/route"))
            {
                return SelectGatewayFromLinuxProcRoute(interfaceName, File.ReadLines("/proc/net/route"));
            }

            if (OsInfo.IsOsx)
            {
                return TryGetGatewayFromMacNetstat(interfaceName);
            }

            if (OsInfo.IsWindows)
            {
                return TryGetGatewayFromWindowsRoutePrint(interfaceName);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static IPAddress SelectGatewayFromLinuxProcRoute(string interfaceName, IEnumerable<string> lines)
    {
        if (lines == null)
        {
            return null;
        }

        IPAddress bestDefault = null;
        var bestDefaultMetric = int.MaxValue;
        IPAddress bestOther = null;
        var bestOtherMetric = int.MaxValue;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Iface", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8)
            {
                continue;
            }

            if (!string.Equals(parts[0], interfaceName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!int.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags) ||
                (flags & RtfUp) == 0 ||
                (flags & RtfGateway) == 0)
            {
                continue;
            }

            var gateway = ParseLinuxProcRouteAddress(parts[2]);
            if (gateway == null || !NatPmpPortMapperService.IsValidGatewayAddress(gateway))
            {
                continue;
            }

            if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var metric))
            {
                metric = int.MaxValue;
            }

            var destination = ParseLinuxProcRouteAddress(parts[1]);
            var isDefault = destination != null && destination.Equals(IPAddress.Any);

            if (isDefault)
            {
                if (metric < bestDefaultMetric)
                {
                    bestDefaultMetric = metric;
                    bestDefault = gateway;
                }
            }
            else if (metric < bestOtherMetric)
            {
                bestOtherMetric = metric;
                bestOther = gateway;
            }
        }

        return bestDefault ?? bestOther;
    }

    private static IPAddress ParseLinuxProcRouteAddress(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 8)
        {
            return null;
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var bytes = new byte[4];
        bytes[0] = (byte)(value & 0xFF);
        bytes[1] = (byte)((value >> 8) & 0xFF);
        bytes[2] = (byte)((value >> 16) & 0xFF);
        bytes[3] = (byte)((value >> 24) & 0xFF);
        return new IPAddress(bytes);
    }

    private static IPAddress TryGetGatewayFromMacNetstat(string interfaceName)
    {
        var output = RunCommand("netstat", $"-nr -f inet -I {interfaceName}", TimeSpan.FromSeconds(2));
        if (string.IsNullOrWhiteSpace(output))
        {
            output = RunCommand("netstat", "-nr -f inet", TimeSpan.FromSeconds(2));
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        IPAddress bestDefault = null;
        IPAddress bestOther = null;

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Routing", StringComparison.Ordinal) || line.StartsWith("Destination", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            if (!string.Equals(parts[^1], interfaceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = parts[0];
            var gatewayToken = parts[1];
            if (!IPAddress.TryParse(gatewayToken, out var gateway) || !NatPmpPortMapperService.IsValidGatewayAddress(gateway))
            {
                continue;
            }

            if (string.Equals(destination, "default", StringComparison.OrdinalIgnoreCase))
            {
                bestDefault = gateway;
            }
            else
            {
                bestOther ??= gateway;
            }
        }

        return bestDefault ?? bestOther;
    }

    private static IPAddress TryGetGatewayFromWindowsRoutePrint(string interfaceName)
    {
        var output = RunCommand("route", "print -4", TimeSpan.FromSeconds(3));
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        IPAddress bestDefault = null;
        var bestDefaultMetric = int.MaxValue;
        IPAddress bestOther = null;
        var bestOtherMetric = int.MaxValue;

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("====", StringComparison.Ordinal) || line.StartsWith("Network Destination", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            if (!IPAddress.TryParse(parts[0], out var destination) ||
                !IPAddress.TryParse(parts[2], out var gateway) ||
                !NatPmpPortMapperService.IsValidGatewayAddress(gateway))
            {
                continue;
            }

            var matchesInterface = IPAddress.TryParse(parts[3], out var ifaceIp) &&
                                   InterfaceHasUnicastAddress(interfaceName, ifaceIp);

            if (!matchesInterface)
            {
                continue;
            }

            var metric = int.MaxValue;
            if (parts.Length >= 5 && int.TryParse(parts[^1], out var parsedMetric))
            {
                metric = parsedMetric;
            }

            if (destination.Equals(IPAddress.Any))
            {
                if (metric < bestDefaultMetric)
                {
                    bestDefaultMetric = metric;
                    bestDefault = gateway;
                }
            }
            else if (metric < bestOtherMetric)
            {
                bestOtherMetric = metric;
                bestOther = gateway;
            }
        }

        return bestDefault ?? bestOther;
    }

    private static bool InterfaceHasUnicastAddress(string interfaceName, IPAddress address)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!string.Equals(nic.Name, interfaceName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(nic.Id, interfaceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address != null && unicast.Address.Equals(address))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string RunCommand(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
                Arguments = arguments, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo); /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
            if (process == null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                return null;
            }

            return stdout;
        }
        catch
        {
            return null;
        }
    }
}
