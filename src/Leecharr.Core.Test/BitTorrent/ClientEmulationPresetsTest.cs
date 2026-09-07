// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
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

    [TestCase("-qB4420-", "qB4420")]
    [TestCase("-DE2050-", "DE2050")]
    [TestCase("-TR3000-", "TR3000")]
    [TestCase("-UT3550-", "UT3550")]
    [TestCase("-AZ3400-", "AZ3400")]
    [TestCase("-LC1000-", "LC1000")]
    [TestCase("", "qB4420")]
    [TestCase(null, "qB4420")]
    public void CleanClientVersion_ExtractsExpectedVersion(string peerIdPrefix, string expectedVersion)
    {
        ClientEmulationPresets.CleanClientVersion(peerIdPrefix).Should().Be(expectedVersion);
    }

    [TestCase("-qB4420-", "qB")]
    [TestCase("-DE2050-", "DE")]
    [TestCase("-TR3000-", "TR")]
    [TestCase("-UT3550-", "UT")]
    [TestCase("-AZ3400-", "AZ")]
    [TestCase("-LC1000-", "LC")]
    [TestCase("", "qB")]
    [TestCase(null, "qB")]
    public void CleanClientIdentifier_ExtractsExpectedIdentifier(string peerIdPrefix, string expectedIdentifier)
    {
        ClientEmulationPresets.CleanClientIdentifier(peerIdPrefix).Should().Be(expectedIdentifier);
    }

    [Test]
    public void Presets_NeverContainMonoTorrentOrMO3002()
    {
        var clients = new[] { "qBittorrent", "Deluge", "Transmission", "uTorrent", "BiglyBT", "Leecharr", null, "unknown" };
        foreach (var client in clients)
        {
            var (userAgent, peerIdPrefix) = ClientEmulationPresets.GetPreset(client);
            userAgent.Should().NotContain("MonoTorrent");
            userAgent.Should().NotContain("MO3002");
            peerIdPrefix.Should().NotContain("MO3002");
            peerIdPrefix.Should().NotStartWith("-MO");
        }
    }

    [Test]
    public void ConfigureGlobalMonoTorrentDefaults_PatchesAllMonoTorrentAssembliesAndDhtMessage()
    {
        MonoTorrentDownloadEngine.ConfigureGlobalMonoTorrentDefaults("-qB4420-");

        var monoTorrentAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("MonoTorrent", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        monoTorrentAssemblies.Should().NotBeEmpty();

        foreach (var asm in monoTorrentAssemblies)
        {
            var gitInfoType = asm.GetType("MonoTorrent.GitInfoHelper");
            if (gitInfoType != null)
            {
                var clientVer = gitInfoType.GetProperty("ClientVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as string;
                var dhtVer = gitInfoType.GetProperty("DhtClientVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as string;

                clientVer.Should().Be("qB4420");
                dhtVer.Should().Be("qB4420");
            }

            var dhtMsgType = asm.GetType("MonoTorrent.Dht.Messages.DhtMessage");
            if (dhtMsgType != null)
            {
                var dhtVersionField = dhtMsgType.GetField("DhtVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?? dhtMsgType.GetField("<DhtVersion>k__BackingField", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var dhtVersionVal = dhtVersionField?.GetValue(null)?.ToString();

                dhtVersionVal.Should().Be("qB4420");
            }
        }
    }
}
