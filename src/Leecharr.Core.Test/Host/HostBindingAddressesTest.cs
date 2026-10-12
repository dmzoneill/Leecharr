// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Host;

namespace Leecharr.Core.Test.Host;

[TestFixture]
public class HostBindingAddressesTest
{
    [TestCase("localhost", true)]
    [TestCase("Localhost", true)]
    [TestCase(" localhost ", true)]
    [TestCase("127.0.0.1", false)]
    [TestCase("::1", false)]
    [TestCase("*", false)]
    public void ShouldUseListenLocalhost_MatchesExpected(string bindAddress, bool expected)
    {
        HostBindingAddresses.ShouldUseListenLocalhost(bindAddress).Should().Be(expected);
    }

    [Test]
    public void ResolveListenAddress_ParsesLiteralAddress()
    {
        HostBindingAddresses.ResolveListenAddress("192.168.1.10").Should().Be(IPAddress.Parse("192.168.1.10"));
    }

    [Test]
    public void ResolveListenAddress_DoesNotFailOpenToAny_WhenHostnameDoesNotResolve()
    {
        HostBindingAddresses.ResolveListenAddress("not-a-valid-bind").Should().Be(IPAddress.Loopback);
        HostBindingAddresses.ResolveListenAddress("leecharr-missing-if-1134").Should().Be(IPAddress.Loopback);
    }
}
