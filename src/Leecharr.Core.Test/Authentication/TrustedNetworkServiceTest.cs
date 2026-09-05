// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class TrustedNetworkServiceTest
{
    private TrustedNetworkService service;

    [SetUp]
    public void SetUp()
    {
        this.service = new TrustedNetworkService();
    }

    [TestCase("127.0.0.1", true)]
    [TestCase("::1", true)]
    [TestCase("::ffff:127.0.0.1", true)]
    [TestCase("10.1.2.3", true)]
    [TestCase("::ffff:10.1.2.3", true)]
    [TestCase("172.20.10.5", true)]
    [TestCase("192.168.1.100", true)]
    [TestCase("8.8.8.8", false)]
    [TestCase("1.1.1.1", false)]
    public void IsLocalOrPrivateNetwork_ShouldIdentifyCorrectly(string ipStr, bool expected)
    {
        var ip = IPAddress.Parse(ipStr);
        var result = this.service.IsLocalOrPrivateNetwork(ip);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("10.0.0.5", "10.0.0.0/8", true)]
    [TestCase("::ffff:10.0.0.5", "10.0.0.0/8", true)]
    [TestCase("::ffff:10.0.0.2", "10.0.0.2", true)]
    [TestCase("::ffff:127.0.0.1", "10.0.0.0/8", true)]
    [TestCase("192.168.1.50", "192.168.1.0/24, 10.0.0.0/8", true)]
    [TestCase("192.168.2.50", "192.168.1.0/24", false)]
    public void IsTrustedProxy_WithCidrs_ShouldMatchCorrectly(string ipStr, string cidrs, bool expected)
    {
        var ip = IPAddress.Parse(ipStr);
        var result = this.service.IsTrustedProxy(ip, cidrs);

        Assert.That(result, Is.EqualTo(expected));
    }
}
