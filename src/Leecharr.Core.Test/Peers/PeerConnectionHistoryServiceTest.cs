// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DryIoc;
using FluentAssertions;
using MonoTorrent.Client;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Peers;
using Arg = NSubstitute.Arg;

namespace Leecharr.Core.Test.Peers;

[TestFixture]
public class PeerConnectionHistoryServiceTest
{
    [Test]
    public void RegisterSingletonWithInterfaces_PeerConnectionHistoryService_ResolvesSameInstanceAcrossInterfacesAndConcrete()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        var geoIpService = Substitute.For<IGeoIpService>();
        container.RegisterInstance(geoIpService);
        container.RegisterSingletonWithInterfaces<PeerConnectionHistoryService>();

        var byInterface = container.Resolve<IPeerConnectionHistoryService>();
        var byConcrete = container.Resolve<PeerConnectionHistoryService>();
        var byInterfaceSecondTime = container.Resolve<IPeerConnectionHistoryService>();

        byInterface.Should().NotBeNull();
        byConcrete.Should().NotBeNull();
        byInterface.Should().BeSameAs(byConcrete);
        byInterface.Should().BeSameAs(byInterfaceSecondTime);
    }

    [Test]
    public void Handle_WhenPeerConnectionEventPublished_RecordsEventInHistory()
    {
        var geoIpService = Substitute.For<IGeoIpService>();
        var service = new PeerConnectionHistoryService(geoIpService);

        var connectionEvent = new PeerConnectionEvent
        {
            InfoHash = "testhash",
            TorrentName = "Test Torrent",
            RemoteIp = "192.168.1.100",
            RemotePort = 6881,
            PeerId = "qBittorrent/4.3.9",
            EventType = "Connected",
            Timestamp = DateTime.UtcNow,
        };

        service.Handle(connectionEvent);

        var records = service.GetRecords();
        records.Should().HaveCount(1);
        records[0].InfoHash.Should().Be("testhash");
        records[0].RemoteIp.Should().Be("192.168.1.100");
        records[0].EventType.Should().Be("Connected");
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPeerConnected_RecordsEventInPeerConnectionHistoryService()
    {
        var historyService = Substitute.For<IPeerConnectionHistoryService>();
        var task = new MonoTorrentDownloadTask(
            1,
            "test-hash",
            null,
            peerConnectionHistoryService: historyService);

        var args = (PeerConnectedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PeerConnectedEventArgs));
        var onPeerConnectedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPeerConnected", BindingFlags.NonPublic | BindingFlags.Instance);
        onPeerConnectedMethod.Should().NotBeNull();

        onPeerConnectedMethod!.Invoke(task, new object[] { null!, args });

        historyService.Received(1).RecordEvent(Arg.Is<PeerConnectionEvent>(e =>
            e.InfoHash == "test-hash" &&
            e.EventType == "Connected"));
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPeerDisconnected_RecordsEventInPeerConnectionHistoryService()
    {
        var historyService = Substitute.For<IPeerConnectionHistoryService>();
        var task = new MonoTorrentDownloadTask(
            2,
            "test-hash-2",
            null,
            peerConnectionHistoryService: historyService);

        var args = (PeerDisconnectedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PeerDisconnectedEventArgs));
        var onPeerDisconnectedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPeerDisconnected", BindingFlags.NonPublic | BindingFlags.Instance);
        onPeerDisconnectedMethod.Should().NotBeNull();

        onPeerDisconnectedMethod!.Invoke(task, new object[] { null!, args });

        historyService.Received(1).RecordEvent(Arg.Is<PeerConnectionEvent>(e =>
            e.InfoHash == "test-hash-2" &&
            e.EventType == "Disconnected"));
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPeerConnected_WhenHistoryServiceNull_PublishesToEventAggregator()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var task = new MonoTorrentDownloadTask(
            3,
            "test-hash-3",
            null,
            eventAggregator: eventAggregator);

        var args = (PeerConnectedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PeerConnectedEventArgs));
        var onPeerConnectedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPeerConnected", BindingFlags.NonPublic | BindingFlags.Instance);
        onPeerConnectedMethod.Should().NotBeNull();

        onPeerConnectedMethod!.Invoke(task, new object[] { null!, args });

        eventAggregator.Received(1).PublishEvent(Arg.Is<PeerConnectionEvent>(e =>
            e.InfoHash == "test-hash-3" &&
            e.EventType == "Connected"));
    }
}
