// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class TrackerBindAddressHelperTest
{
    [Test]
    public void ResolveBindAddress_DefaultsToAny_WhenUnsetOrWildcard()
    {
        TrackerBindAddressHelper.ResolveBindAddress(null).Should().Be(IPAddress.Any);
        TrackerBindAddressHelper.ResolveBindAddress("").Should().Be(IPAddress.Any);
        TrackerBindAddressHelper.ResolveBindAddress("0.0.0.0").Should().Be(IPAddress.Any);
        TrackerBindAddressHelper.ResolveBindAddress("*").Should().Be(IPAddress.Any);
    }

    [Test]
    public void ResolveBindAddress_ParsesLiteralAddress()
    {
        TrackerBindAddressHelper.ResolveBindAddress("127.0.0.1").Should().Be(IPAddress.Loopback);
    }

    [Test]
    public void IsHttpRequestAllowed_AllowsAnyInterface_WhenBindIsWildcard()
    {
        TrackerBindAddressHelper.IsHttpRequestAllowed("0.0.0.0", IPAddress.Parse("192.168.1.5")).Should().BeTrue();
    }

    [Test]
    public void IsHttpRequestAllowed_RequiresMatchingLocalEndpoint_WhenBindIsSpecific()
    {
        var lan = IPAddress.Parse("192.168.1.10");
        TrackerBindAddressHelper.IsHttpRequestAllowed("192.168.1.10", lan).Should().BeTrue();
        TrackerBindAddressHelper.IsHttpRequestAllowed("192.168.1.10", IPAddress.Parse("10.0.0.5")).Should().BeFalse();
    }

    [Test]
    public void IsHttpRequestAllowed_AllowsLoopbackFamily_WhenBoundToLoopback()
    {
        TrackerBindAddressHelper.IsHttpRequestAllowed("127.0.0.1", IPAddress.Loopback).Should().BeTrue();
        TrackerBindAddressHelper.IsHttpRequestAllowed("127.0.0.1", IPAddress.IPv6Loopback).Should().BeTrue();
    }
}
