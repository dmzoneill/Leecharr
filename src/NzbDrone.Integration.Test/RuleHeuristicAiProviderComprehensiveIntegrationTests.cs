// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class RuleHeuristicAiProviderComprehensiveIntegrationTests : IntegrationTestBase
{
    private RuleHeuristicAiProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.provider = new RuleHeuristicAiProvider();
    }

    [Test]
    public async Task RuleHeuristicAi_ParseReleaseAsync_ExtractsRichSceneAttributes()
    {
        // 1. Complex TV show release
        const string tvRelease = "Breaking.Bad.S01E01-E03.2160p.UHD.BluRay.REMUX.HEVC.DV.HDR10.TrueHD.Atmos.7.1-FraMeSToR";
        var parsedTv = await this.provider.ParseReleaseAsync(tvRelease);

        parsedTv.Should().NotBeNull();
        parsedTv.CleanTitle.Should().Be("Breaking Bad");
        parsedTv.Season.Should().Be(1);
        parsedTv.Episode.Should().Be(1);
        parsedTv.Episodes.Should().Contain(new[] { 1, 2, 3 });
        parsedTv.Resolution.Should().Be("2160p");
        parsedTv.IsRemux.Should().BeTrue();
        parsedTv.VideoCodec.Should().Be("x265");
        parsedTv.DynamicRange.Should().Be("Dolby Vision");
        parsedTv.AudioCodec.Should().ContainEquivalentOf("TrueHD");
        parsedTv.AudioChannels.Should().Be("7.1");
        parsedTv.ReleaseGroup.Should().Be("FraMeSToR");

        // 2. Movie with Repack and Year
        const string movieRelease = "Inception.2010.REPACK.1080p.BluRay.x264.DTS-HD.MA.5.1-DON";
        var parsedMovie = await this.provider.ParseReleaseAsync(movieRelease);

        parsedMovie.Should().NotBeNull();
        parsedMovie.CleanTitle.Should().Be("Inception");
        parsedMovie.Year.Should().Be(2010);
        parsedMovie.IsRepack.Should().BeTrue();
        parsedMovie.Resolution.Should().Be("1080p");
        parsedMovie.AudioCodec.Should().ContainEquivalentOf("DTS-HD");
        parsedMovie.AudioChannels.Should().Be("5.1");
        parsedMovie.ReleaseGroup.Should().Be("DON");
    }

    [Test]
    public async Task RuleHeuristicAi_NaturalLanguageSearch_TranslatesQueries()
    {
        var nlp1 = await this.provider.ProcessNaturalLanguageSearchAsync("Find Oppenheimer 2023 in 4k with Dolby Vision and Atmos");
        nlp1.Should().NotBeNull();
        nlp1.CleanTitle.Should().Contain("Oppenheimer");
        nlp1.Year.Should().Be(2023);
        nlp1.Resolution.Should().Be("2160p");

        var nlp2 = await this.provider.ProcessNaturalLanguageSearchAsync("Season 2 of Stranger Things 1080p");
        nlp2.Should().NotBeNull();
        nlp2.CleanTitle.Should().Contain("Stranger Things");
        nlp2.Season.Should().Be(2);
        nlp2.Resolution.Should().Be("1080p");
    }

    [Test]
    public async Task RuleHeuristicAi_DiagnoseTorrentHealthAsync_AnalyzesSwarmHealthAccurately()
    {
        // 1. Stalled swarm with 0 seeds and 0 peers
        var stalledTorrent = new Torrent
        {
            Id = 1,
            Name = "StalledMovie",
            Status = TorrentStatus.Downloading,
            Progress = 0.3,
            Seeders = 0,
            Leechers = 0,
            DownloadSpeed = 0,
        };

        var trackers = new List<TrackerEntry>
        {
            new() { Url = "udp://tracker.test:1337", ErrorMessage = "Connection timed out", ConsecutiveFailures = 4 },
        };

        var stalledDiag = await this.provider.DiagnoseTorrentHealthAsync(stalledTorrent, new List<PeerInfo>(), trackers);
        stalledDiag.Should().NotBeNull();
        stalledDiag.HealthScore.Should().BeLessThan(50);
        stalledDiag.Issues.Should().NotBeEmpty();
        stalledDiag.Recommendations.Should().NotBeEmpty();
        stalledDiag.SuggestedActions.Should().NotBeEmpty();

        // 2. Seeding torrent with completed ratio
        var seedingTorrent = new Torrent
        {
            Id = 2,
            Name = "SeedingMovie",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 2.5,
            TargetRatio = 2.0,
            Seeders = 10,
            Leechers = 0,
        };

        var seedingDiag = await this.provider.DiagnoseTorrentHealthAsync(seedingTorrent, new List<PeerInfo>(), new List<TrackerEntry>());
        seedingDiag.Should().NotBeNull();
        seedingDiag.Severity.Should().Be("None");
    }

    [Test]
    public async Task RuleHeuristicAi_AnalyzeMalwareRiskAsync_CatchesDangerousFiles()
    {
        // 1. Dangerous executable masquerading as a movie
        var dangerousFiles = new List<TorrentFile>
        {
            new() { Path = "Great.Movie.2024.1080p.mkv.exe", Size = 1024 * 1024 * 2 },
            new() { Path = "instructions.bat", Size = 512 },
        };

        var threatReport = await this.provider.AnalyzeMalwareRiskAsync("Great.Movie.2024.1080p.mkv.exe", dangerousFiles);
        threatReport.Should().NotBeNull();
        threatReport.IsSuspicious.Should().BeTrue();
        threatReport.RiskLevel.Should().BeOneOf("High", "Critical");
        threatReport.ThreatReasons.Should().NotBeEmpty();

        // 2. Legitimate media torrent
        var safeFiles = new List<TorrentFile>
        {
            new() { Path = "SafeMovie.2024.1080p.mkv", Size = 1024L * 1024L * 1024L * 4L },
            new() { Path = "SafeMovie.2024.1080p.en.srt", Size = 65536 },
        };

        var safeReport = await this.provider.AnalyzeMalwareRiskAsync("SafeMovie.2024.1080p.mkv", safeFiles);
        safeReport.Should().NotBeNull();
        safeReport.IsSuspicious.Should().BeFalse();
        safeReport.RiskLevel.Should().Be("Safe");
    }

    [Test]
    public async Task RuleHeuristicAi_CopilotAndProbeHealth_Succeeds()
    {
        var health = await this.provider.ProbeHealthAsync();
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();

        var copilot = await this.provider.GenerateChatResponseAsync("How do I fix a stalled torrent?", null);
        copilot.Should().NotBeNullOrWhiteSpace();
        copilot.Should().Contain("Diagnostics & Speed Troubleshooting");
    }
}
