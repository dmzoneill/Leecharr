// Copyright (c) FeedItOut. All rights reserved.

using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentContentLayoutPlannerTest
{
    [Test]
    public void Plan_Subfolder_PrefixesFlatFilesWithTorrentName()
    {
        var plan = TorrentContentLayoutPlanner.Plan(
            TorrentContentLayout.Subfolder,
            "ShowName",
            new[] { "episode.mkv", "extras/bonus.nfo" });

        plan.Should().BeEquivalentTo(new[]
        {
            ("episode.mkv", "ShowName/episode.mkv"),
            ("extras/bonus.nfo", "ShowName/extras/bonus.nfo"),
        });
    }

    [Test]
    public void Plan_Subfolder_LeavesSharedRootUntouched()
    {
        var plan = TorrentContentLayoutPlanner.Plan(
            TorrentContentLayout.Subfolder,
            "ShowName",
            new[] { "ShowName/episode.mkv", "showname/extras/bonus.nfo" });

        plan.Should().BeEmpty();
    }

    [Test]
    public void Plan_NoSubfolder_StripsSharedRoot()
    {
        var plan = TorrentContentLayoutPlanner.Plan(
            TorrentContentLayout.NoSubfolder,
            "ShowName",
            new[] { @"Season\episode.mkv", "Season/extras/bonus.nfo" });

        plan.Select(pair => (pair.OldPath, pair.NewPath)).Should().BeEquivalentTo(new[]
        {
            ("Season/episode.mkv", "episode.mkv"),
            ("Season/extras/bonus.nfo", "extras/bonus.nfo"),
        });
    }

    [Test]
    public void Plan_NoSubfolder_LeavesFlatFilesUntouched()
    {
        var plan = TorrentContentLayoutPlanner.Plan(
            TorrentContentLayout.NoSubfolder,
            "ShowName",
            new[] { "episode.mkv", "Other/bonus.nfo" });

        plan.Should().BeEmpty();
    }

    [Test]
    public void Plan_Original_DoesNotRename()
    {
        var plan = TorrentContentLayoutPlanner.Plan(
            TorrentContentLayout.Original,
            "ShowName",
            new[] { "Season/episode.mkv" });

        plan.Should().BeEmpty();
    }
}
