using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class RssSyncServiceTest
{
    private IIndexerRepository _indexerRepository = null!;
    private IRssRuleRepository _rssRuleRepository = null!;
    private ITorznabClient _torznabClient = null!;
    private ITorrentService _torrentService = null!;
    private RssSyncService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerRepository = Substitute.For<IIndexerRepository>();
        _rssRuleRepository = Substitute.For<IRssRuleRepository>();
        _torznabClient = Substitute.For<ITorznabClient>();
        _torrentService = Substitute.For<ITorrentService>();

        _service = new RssSyncService(
            _indexerRepository,
            _rssRuleRepository,
            _torznabClient,
            _torrentService);
    }

    [Test]
    public void MatchesRule_WhenMustContainMatches_ReturnsTrue()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.2160p.WEB-DL.x265",
            Seeders = 10,
            Size = 5000000000
        };

        var rule = new RssRule
        {
            Name = "Severance 2160p",
            IsEnabled = true,
            MustContain = "Severance.*2160p",
            MinSeeders = 5
        };

        _service.MatchesRule(release, rule).Should().BeTrue();
    }

    [Test]
    public void MatchesRule_WhenMustNotContainMatches_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.720p.HDTV.x264",
            Seeders = 10
        };

        var rule = new RssRule
        {
            Name = "No 720p",
            IsEnabled = true,
            MustNotContain = "720p|HDTV"
        };

        _service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenBelowMinSeeders_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Dune.Part.Two.2024.2160p",
            Seeders = 2
        };

        var rule = new RssRule
        {
            Name = "High Seeders Only",
            IsEnabled = true,
            MinSeeders = 10
        };

        _service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenFreeleechOnlyRequiredAndNotFreeleech_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Sample Release",
            Seeders = 10,
            DownloadVolumeFactor = 1.0
        };

        var rule = new RssRule
        {
            Name = "Freeleech Only",
            IsEnabled = true,
            FreeleechOnly = true
        };

        _service.MatchesRule(release, rule).Should().BeFalse();
    }
}
