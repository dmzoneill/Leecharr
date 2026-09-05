// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class ClientEmulationPresetsTest
{
    [TestCase("qBittorrent", "qBittorrent/4.4.2", "-qB4420-")]
    [TestCase("Deluge", "Deluge/2.0.5 libtorrent/1.2.14.0", "-DE2050-")]
    [TestCase("Transmission", "Transmission/3.00", "-TR3000-")]
    [TestCase("uTorrent", "uTorrent/3550", "-UT3550-")]
    [TestCase("BiglyBT", "BiglyBT/3.4.0.0", "-AZ3400-")]
    [TestCase("Leecharr", "Leecharr/1.0.0", "-LC1000-")]
    [TestCase(null, "qBittorrent/4.4.2", "-qB4420-")]
    [TestCase("unknown", "qBittorrent/4.4.2", "-qB4420-")]
    public void GetPreset_ReturnsExpectedUserAgentAndPeerId(string client, string expectedUserAgent, string expectedPeerIdPrefix)
    {
        var (userAgent, peerIdPrefix) = ClientEmulationPresets.GetPreset(client);

        userAgent.Should().Be(expectedUserAgent);
        peerIdPrefix.Should().Be(expectedPeerIdPrefix);
    }
}
