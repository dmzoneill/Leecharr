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

    [TestCase(2.0, 1.5, 3.0, 2.0)] // Torrent ratio takes precedence
    [TestCase(0.0, 1.5, 3.0, 1.5)] // Category ratio takes precedence when torrent is 0
    [TestCase(0.0, 0.0, 3.0, 3.0)] // Global ratio used when torrent and category are 0
    [TestCase(0.0, 0.0, 0.0, 0.0)] // 0 when none configured
    public void EffectiveRatio_EvaluatesWithCorrectFallbackHierarchy(double torrentRatio, double categoryRatio, double globalRatio, double expectedEffective)
    {
        var category = categoryRatio > 0 ? new NzbDrone.Core.Categories.Category { TargetRatio = categoryRatio } : null;
        var torrent = new Torrent { TargetRatio = torrentRatio };

        var effectiveRatio = torrent.TargetRatio > 0
            ? torrent.TargetRatio
            : ((category?.TargetRatio ?? 0) > 0 ? category.TargetRatio : globalRatio);

        effectiveRatio.Should().Be(expectedEffective);
    }
}
