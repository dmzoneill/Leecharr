using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class MultiTrackerAnnouncePolicyTest
{
    [Test]
    public void ResolveAnnounceScope_WhenMultiTrackerDisabled_IgnoresParallelFlags()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(false);
        config.AnnounceToAllInTier.Returns(true);
        config.AnnounceToAllTiers.Returns(true);

        MultiTrackerAnnouncePolicy.ResolveAnnounceScope(config).Should().Be(MultiTrackerAnnounceScope.Default);
    }

    [Test]
    public void ResolveAnnounceScope_PrefersAllInTierOverAllTiers()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.AnnounceToAllInTier.Returns(true);
        config.AnnounceToAllTiers.Returns(true);

        MultiTrackerAnnouncePolicy.ResolveAnnounceScope(config).Should().Be(MultiTrackerAnnounceScope.AllInTier);
    }

    [Test]
    public void ResolveLibTorrentAnnounceSettings_WhenAllInTier_EnablesBothFlags()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.AnnounceToAllInTier.Returns(true);
        config.AnnounceToAllTiers.Returns(false);

        var (announceToAllTiers, announceToAllTrackers) = MultiTrackerAnnouncePolicy.ResolveLibTorrentAnnounceSettings(config);

        announceToAllTiers.Should().BeTrue();
        announceToAllTrackers.Should().BeTrue();
    }

    [Test]
    public void ResolveLibTorrentAnnounceSettings_WhenAllTiersOnly_EnablesTierFlagOnly()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.AnnounceToAllInTier.Returns(false);
        config.AnnounceToAllTiers.Returns(true);

        var (announceToAllTiers, announceToAllTrackers) = MultiTrackerAnnouncePolicy.ResolveLibTorrentAnnounceSettings(config);

        announceToAllTiers.Should().BeTrue();
        announceToAllTrackers.Should().BeFalse();
    }

    [Test]
    public void ResolveLibTorrentAnnounceSettings_WhenMultiTrackerDisabled_DisablesBothFlags()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(false);
        config.AnnounceToAllInTier.Returns(true);
        config.AnnounceToAllTiers.Returns(true);

        var (announceToAllTiers, announceToAllTrackers) = MultiTrackerAnnouncePolicy.ResolveLibTorrentAnnounceSettings(config);

        announceToAllTiers.Should().BeFalse();
        announceToAllTrackers.Should().BeFalse();
    }

    [Test]
    public void ResolveLibTorrentAnnounceSettings_WhenFlagsOff_DisablesBothFlags()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.AnnounceToAllInTier.Returns(false);
        config.AnnounceToAllTiers.Returns(false);

        var (announceToAllTiers, announceToAllTrackers) = MultiTrackerAnnouncePolicy.ResolveLibTorrentAnnounceSettings(config);

        announceToAllTiers.Should().BeFalse();
        announceToAllTrackers.Should().BeFalse();
    }

    [Test]
    public void ComputeBackoffSeconds_DoublesUntilCapped()
    {
        MultiTrackerAnnouncePolicy.ComputeBackoffSeconds(1, 30, 1800).Should().Be(30);
        MultiTrackerAnnouncePolicy.ComputeBackoffSeconds(2, 30, 1800).Should().Be(60);
        MultiTrackerAnnouncePolicy.ComputeBackoffSeconds(10, 30, 1800).Should().Be(1800);
    }

    [Test]
    public void IsFailoverEnabled_RequiresMultiTrackerAndFailoverToggles()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.MultiTrackerFailoverEnabled.Returns(true);
        MultiTrackerAnnouncePolicy.IsFailoverEnabled(config).Should().BeTrue();

        config.MultiTrackerEnabled.Returns(false);
        MultiTrackerAnnouncePolicy.IsFailoverEnabled(config).Should().BeFalse();
    }
}
