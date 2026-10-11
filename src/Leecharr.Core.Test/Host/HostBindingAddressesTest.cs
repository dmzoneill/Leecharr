// Copyright (c) FeedItOut. All rights reserved.

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
}
