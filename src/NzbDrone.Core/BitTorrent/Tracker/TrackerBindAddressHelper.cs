// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NzbDrone.Core.Network.Binding;

namespace NzbDrone.Core.BitTorrent.Tracker;

public static class TrackerBindAddressHelper
{
    public static IPAddress ResolveBindAddress(string bindAddressStr)
    {
        if (string.IsNullOrWhiteSpace(bindAddressStr))
        {
            return IPAddress.Any;
        }

        var trimmed = bindAddressStr.Trim();
        if (IsWildcardBindAddress(trimmed))
        {
            return IPAddress.Any;
        }

        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            return parsed;
        }

        var fromInterface = ManagedSocketBindingProvider.GetInterfaceIp(trimmed, AddressFamily.InterNetwork)
            ?? ManagedSocketBindingProvider.GetInterfaceIp(trimmed, AddressFamily.InterNetworkV6);
        if (fromInterface != null)
        {
            return fromInterface;
        }

        try
        {
            var hostAddresses = Dns.GetHostAddresses(trimmed);
            var resolved = hostAddresses.FirstOrDefault(ManagedSocketBindingProvider.IsUsableBindUnicastAddress)
                ?? hostAddresses.FirstOrDefault(IPAddress.IsLoopback);
            if (resolved != null)
            {
                return resolved;
            }
        }
        catch (SocketException)
        {
        }
        catch (ArgumentException)
        {
        }

        // Operator set a non-wildcard bind value we could not resolve; do not listen on every interface.
        return IPAddress.Loopback;
    }

    private static bool IsWildcardBindAddress(string bindAddressStr)
    {
        return bindAddressStr == "0.0.0.0"
               || bindAddressStr == "*"
               || string.Equals(bindAddressStr, "::", StringComparison.Ordinal);
    }

    public static bool BindsAllInterfaces(IPAddress bindAddress)
    {
        if (bindAddress == null)
        {
            return true;
        }

        return bindAddress.Equals(IPAddress.Any)
               || bindAddress.Equals(IPAddress.IPv6Any);
    }

    public static bool IsLocalEndpointAllowed(IPAddress localAddress, IPAddress configuredBindAddress)
    {
        if (BindsAllInterfaces(configuredBindAddress))
        {
            return true;
        }

        if (localAddress == null)
        {
            return false;
        }

        localAddress = NormalizeAddress(localAddress);
        configuredBindAddress = NormalizeAddress(configuredBindAddress);

        if (localAddress.Equals(configuredBindAddress))
        {
            return true;
        }

        if (IPAddress.IsLoopback(configuredBindAddress) && IPAddress.IsLoopback(localAddress))
        {
            return true;
        }

        return false;
    }

    public static bool IsHttpRequestAllowed(string bindAddressStr, IPAddress localAddress)
    {
        return IsLocalEndpointAllowed(localAddress, ResolveBindAddress(bindAddressStr));
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        if (address != null && address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address;
    }
}
