// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class ShareLimitActionTest
{
    [Test]
    public void Torrent_DefaultsShareLimitActionToPause()
    {
        var torrent = new Torrent();
        torrent.ShareLimitAction.Should().Be("Pause");
    }

    [Test]
    public void ConfigService_DefaultsGlobalShareLimitActionToPause()
    {
        var configService = Substitute.For<IConfigService>();
        configService.GlobalShareLimitAction.Returns("Pause");
        configService.GlobalShareLimitAction.Should().Be("Pause");
    }

    [TestCase(1, "Remove")]
    [TestCase(2, "SuperSeeding")]
    [TestCase(3, "RemoveWithData")]
    [TestCase(0, "Pause")]
    [TestCase(-1, "Pause")]
    public void MaxRatioAction_MapsToExpectedShareLimitAction(int maxRatioAction, string expectedAction)
    {
        var action = maxRatioAction switch
        {
            1 => "Remove",
            2 => "SuperSeeding",
            3 => "RemoveWithData",
            _ => "Pause",
        };

        action.Should().Be(expectedAction);
    }
}
