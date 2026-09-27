// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using MonoTorrent;
using MonoTorrent.Client;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class MonoTorrentDownloadTaskComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void MonoTorrentDownloadTask_StateMapping_MapsAllStatesAccurately()
    {
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Downloading).Should().Be(TorrentStatus.Downloading);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Seeding).Should().Be(TorrentStatus.Seeding);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Paused).Should().Be(TorrentStatus.Paused);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Stopped).Should().Be(TorrentStatus.Stopped);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Hashing).Should().Be(TorrentStatus.Checking);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Metadata).Should().Be(TorrentStatus.Downloading);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Starting).Should().Be(TorrentStatus.Downloading);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Stopping).Should().Be(TorrentStatus.Paused);
        MonoTorrentDownloadTask.MapTorrentStateToStatus(TorrentState.Error).Should().Be(TorrentStatus.Error);
    }

    [Test]
    public void MonoTorrentDownloadTask_PeerManagementAndBanning_TracksCorrectly()
    {
        var task = new MonoTorrentDownloadTask(
            torrentId: 99,
            infoHash: "1111222233334444555566667777888899990000",
            manager: null,
            category: "movies",
            isPrivate: true,
            workingPath: "/downloads/working");

        task.TorrentId.Should().Be(99);
        task.InfoHash.Should().Be("1111222233334444555566667777888899990000");
        task.Category.Should().Be("movies");
        task.IsPrivate.Should().BeTrue();
        task.WorkingPath.Should().Be("/downloads/working");

        // 1. Peer banning (BanPeer adds to blocklist, fires events and disconnects)
        task.IsPeerBanned("192.168.1.50").Should().BeFalse();
        task.BanPeer("192.168.1.50");
        task.IsPeerBanned("192.168.1.50").Should().BeFalse(); // Not marked locally in bannedPeers until hash check threshold
        task.IsPeerBanned("192.168.1.51").Should().BeFalse();

        // 2. Peer hash fail counts
        task.GetPeerHashFailCount("192.168.1.50").Should().Be(0);
        task.GetPeerHashFailCount(null).Should().Be(0);
        task.IsPeerBanned("").Should().BeFalse();

        // 3. Sequential and First/Last piece priority setters
        task.SequentialDownload = true;
        task.SequentialDownload.Should().BeTrue();

        task.FirstLastPiecePriority = true;
        task.FirstLastPiecePriority.Should().BeTrue();

        // 4. Recheck & AutoPause flags
        task.IsQueuedForRecheck = true;
        task.IsQueuedForRecheck.Should().BeTrue();

        task.IsExplicitRecheck = true;
        task.IsExplicitRecheck.Should().BeTrue();

        task.WasAutoPausedByDiskSpace = true;
        task.WasAutoPausedByDiskSpace.Should().BeTrue();

        // 5. Block received with null picker returns false
        task.RecordBlockReceived(0, 0, 16384).Should().BeFalse();

        // 6. Check tracker health with null manager returns false
        task.CheckTrackerHealth().Should().BeFalse();

        // 7. RejectRequest does not throw
        task.RejectRequest(0, 0);
        task.RejectRequest(0, 0, 16384, "peer-1");

        // 8. Status with null manager is Stopped
        task.Status.Should().Be(TorrentStatus.Stopped);
    }
}
