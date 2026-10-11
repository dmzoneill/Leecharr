#nullable enable

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Automation;

[TestFixture]
public class ScriptTorrentContextTest
{
    [Test]
    public void isComplete_IsFalse_WhenProgressIs999ThousandthsAndStillDownloading()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Progress = 0.999,
            Status = TorrentStatus.Downloading,
        };
        var ctx = new ScriptTorrentContext(torrent, new AutomationExecutionResult());

        ctx.isComplete.Should().BeFalse();
    }

    [Test]
    public void isComplete_IsTrue_WhenProgressReachesOne()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Progress = 1.0,
            Status = TorrentStatus.Downloading,
        };
        var ctx = new ScriptTorrentContext(torrent, new AutomationExecutionResult());

        ctx.isComplete.Should().BeTrue();
    }

    [Test]
    public void isComplete_IsTrue_WhenSeedingOrDateCompletedSet()
    {
        var seeding = new Torrent { Id = 3, Progress = 0.5, Status = TorrentStatus.Seeding };
        new ScriptTorrentContext(seeding, new AutomationExecutionResult()).isComplete.Should().BeTrue();

        var dated = new Torrent { Id = 4, Progress = 0.5, Status = TorrentStatus.Downloading, DateCompleted = System.DateTime.UtcNow };
        new ScriptTorrentContext(dated, new AutomationExecutionResult()).isComplete.Should().BeTrue();
    }
}
