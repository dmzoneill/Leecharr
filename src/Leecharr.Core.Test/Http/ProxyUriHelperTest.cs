using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Http;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class ProxyUriHelperTest
{
    [TestCase("proxy.example.com", "proxy.example.com")]
    [TestCase("127.0.0.1", "127.0.0.1")]
    [TestCase("::1", "[::1]")]
    [TestCase("2001:db8::1", "[2001:db8::1]")]
    [TestCase("[::1]", "[::1]")]
    public void FormatHostForProxyUri_BracketsIpv6WithoutDoubleWrapping(string host, string expected)
    {
        ProxyUriHelper.FormatHostForProxyUri(host).Should().Be(expected);
    }

    [Test]
    public void BuildProxyUri_WithIpv6Host_DoesNotThrowUriFormatException()
    {
        var uri = ProxyUriHelper.BuildProxyUri("http", "2001:db8::1", 8080);
        uri.ToString().Should().Be("http://[2001:db8::1]:8080/");
    }
}
