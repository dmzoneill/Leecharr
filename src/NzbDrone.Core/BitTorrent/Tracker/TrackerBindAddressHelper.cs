// Copyright (c) FeedItOut. All rights reserved.

using System.Net;

namespace NzbDrone.Core.BitTorrent.Tracker;

public static class TrackerBindAddressHelper
{
    public static IPAddress ResolveBindAddress(string bindAddressStr)
    {
        var bindAddress = IPAddress.Any;

        if (!string.IsNullOrWhiteSpace(bindAddressStr) && bindAddressStr != "0.0.0.0" && bindAddressStr != "*")
        {
            if (IPAddress.TryParse(bindAddressStr, out var parsed))
            {
                bindAddress = parsed;
            }
        }

        return bindAddress;
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
