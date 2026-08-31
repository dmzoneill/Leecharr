// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Ai;

[TestFixture]
public class RuleHeuristicAiProviderTest
{
    private RuleHeuristicAiProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.provider = new RuleHeuristicAiProvider();
    }

    [Test]
    public void Properties_ReturnExpectedValues()
    {
        this.provider.ProviderId.Should().Be("RuleHeuristic");
        this.provider.DisplayName.Should().Contain("Rule-Based");
        this.provider.Version.Should().Be("1.0");
        this.provider.IsAvailable.Should().BeTrue();
        this.provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsReleaseNameParsing);
        this.provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsDiagnosticCopilot);
        this.provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsNaturalLanguageSearch);
        this.provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsMalwareAnomalyDetection);
    }

    [Test]
    public async Task ProbeHealthAsync_ReturnsHealthy()
    {
        var health = await this.provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("operational");
        health.ModelName.Should().Be("Deterministic-Rule-Engine");
    }

    [Test]
    public async Task ParseReleaseAsync_MovieStandard_ParsesCorrectly()
    {
        var release = "Oppenheimer.2023.2160p.UHD.BluRay.x265.10bit.HDR.DTS-HD.MA.5.1-FLUX";
        var result = await this.provider.ParseReleaseAsync(release);

        result.CleanTitle.Should().Be("Oppenheimer");
        result.Year.Should().Be(2023);
        result.Resolution.Should().Be("2160p");
        result.Quality.Should().Be("BluRay");
        result.VideoCodec.Should().Be("x265");
        result.DynamicRange.Should().Be("HDR");
        result.AudioCodec.Should().Be("DTS-HD MA");
        result.AudioChannels.Should().Be("5.1");
        result.ReleaseGroup.Should().Be("FLUX");
        result.ConfidenceScore.Should().BeGreaterThan(0.7);
    }

    [Test]
    public async Task ParseReleaseAsync_TvEpisode_ParsesCorrectly()
    {
        var release = "Breaking.Bad.S05E16.Felina.1080p.WEB-DL.DD5.1.H.264-NTb";
        var result = await this.provider.ParseReleaseAsync(release);

        result.CleanTitle.Should().Be("Breaking Bad");
        result.Season.Should().Be(5);
        result.Episode.Should().Be(16);
        result.Resolution.Should().Be("1080p");
        result.Quality.Should().Be("WEB-DL");
        result.AudioCodec.Should().Be("DD5.1");
        result.ReleaseGroup.Should().Be("NTb");
    }

    [Test]
    public async Task ParseReleaseAsync_MultiEpisode_ParsesEpisodeList()
    {
        var release = "Game.of.Thrones.S01E01-E03.720p.HDTV.x264-Scene";
        var result = await this.provider.ParseReleaseAsync(release);

        result.Season.Should().Be(1);
        result.Episode.Should().Be(1);
        result.Episodes.Should().Contain(new[] { 1, 2, 3 });
        result.Resolution.Should().Be("720p");
        result.Quality.Should().Be("HDTV");
    }

    [Test]
    public async Task ParseReleaseAsync_ProperRepackRemux_ParsesFlags()
    {
        var release = "The.Matrix.1999.2160p.REMUX.PROPER.DV.TrueHD.Atmos.7.1-SPARKS";
        var result = await this.provider.ParseReleaseAsync(release);

        result.Year.Should().Be(1999);
        result.IsRemux.Should().BeTrue();
        result.IsProper.Should().BeTrue();
        result.DynamicRange.Should().Be("Dolby Vision");
        result.AudioCodec.Should().Be("TRUEHD ATMOS");
        result.AudioChannels.Should().Be("7.1");
        result.ReleaseGroup.Should().Be("SPARKS");
    }

    [Test]
    public async Task ParseReleaseAsync_NullOrEmpty_ReturnsZeroConfidence()
    {
        var result = await this.provider.ParseReleaseAsync(string.Empty);
        result.ConfidenceScore.Should().Be(0.0);
        result.CleanTitle.Should().BeEmpty();
    }

    [Test]
    public async Task DiagnoseTorrentHealthAsync_DeadSwarm_ReportsIssues()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Stalled Linux ISO",
            Progress = 0.25,
            Status = TorrentStatus.Downloading,
            Seeders = 0,
            Leechers = 0,
            DownloadSpeed = 0,
        };

        var report = await this.provider.DiagnoseTorrentHealthAsync(torrent, Array.Empty<PeerInfo>(), Array.Empty<TrackerEntry>());

        report.OverallHealth.Should().BeOneOf("Stalled", "Dead");
        report.Issues.Should().NotBeEmpty();
        report.HealthScore.Should().BeLessThan(50);
        report.SuggestedActions.Should().NotBeEmpty();
    }

    [Test]
    public async Task DiagnoseTorrentHealthAsync_HealthySeeding_ReportsNominal()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Ubuntu Desktop 24.04",
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            Ratio = 2.5,
            TargetRatio = 2.0,
            Seeders = 15,
            Leechers = 30,
            UploadSpeed = 1048576,
        };

        var trackers = new List<TrackerEntry>
        {
            new() { Url = "http://tracker.ubuntu.com/announce", Status = 1 },
        };

        var report = await this.provider.DiagnoseTorrentHealthAsync(torrent, Array.Empty<PeerInfo>(), trackers);

        report.OverallHealth.Should().Be("Completed");
        report.HealthScore.Should().BeGreaterThanOrEqualTo(80);
    }

    [Test]
    public async Task DiagnoseTorrentHealthAsync_TrackerFailure_ReportsTrackerAnalysis()
    {
        var torrent = new Torrent
        {
            Id = 3,
            Name = "Arch Linux",
            Progress = 0.1,
            Status = TorrentStatus.Downloading,
            Seeders = 1,
            Leechers = 1,
        };

        var trackers = new List<TrackerEntry>
        {
            new() { Url = "http://bad.tracker/announce", ErrorMessage = "Connection timeout", ConsecutiveFailures = 5 },
        };

        var report = await this.provider.DiagnoseTorrentHealthAsync(torrent, Array.Empty<PeerInfo>(), trackers);

        report.Issues.Should().Contain(i => i.Contains("trackers failed"));
        report.TrackerAnalysis.Should().Contain("1 failing");
    }

    [Test]
    public async Task ProcessNaturalLanguageSearchAsync_ParsesTvIntent()
    {
        var query = "download breaking bad season 2 in 1080p with at least 5 seeders freeleech";
        var result = await this.provider.ProcessNaturalLanguageSearchAsync(query);

        result.Category.Should().Be("tv");
        result.Season.Should().Be(2);
        result.Resolution.Should().Be("1080p");
        result.MinSeeders.Should().Be(5);
        result.FreeleechOnly.Should().BeTrue();
        result.CleanTitle.Should().Contain("breaking bad");
    }

    [Test]
    public async Task ProcessNaturalLanguageSearchAsync_ParsesMovieIntent()
    {
        var query = "find oppenheimer 4k hdr remux";
        var result = await this.provider.ProcessNaturalLanguageSearchAsync(query);

        result.Category.Should().Be("movies");
        result.Resolution.Should().Be("2160p");
        result.Quality.Should().Be("REMUX");
        result.CleanTitle.Should().Contain("oppenheimer");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_SafeMedia_ReturnsSafe()
    {
        var files = new List<TorrentFile>
        {
            new() { Path = "Movie.2024.1080p/Movie.2024.1080p.mkv", Size = 4294967296 },
            new() { Path = "Movie.2024.1080p/Movie.2024.1080p.srt", Size = 45000 },
        };

        var assessment = await this.provider.AnalyzeMalwareRiskAsync("Movie.2024.1080p", files);

        assessment.RiskLevel.Should().Be("Safe");
        assessment.IsSuspicious.Should().BeFalse();
        assessment.RiskScore.Should().BeLessThan(0.2);
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_ExecutableInMediaRelease_ReturnsCriticalOrHigh()
    {
        var files = new List<TorrentFile>
        {
            new() { Path = "Movie.2024.1080p/Movie.mp4.exe", Size = 150000 },
            new() { Path = "Movie.2024.1080p/password_unlocker.bat", Size = 500 },
        };

        var assessment = await this.provider.AnalyzeMalwareRiskAsync("Movie.2024.1080p.WEB-DL", files);

        assessment.IsSuspicious.Should().BeTrue();
        assessment.RiskLevel.Should().BeOneOf("High", "Critical");
        assessment.SuspiciousFileNames.Should().NotBeEmpty();
        assessment.ThreatReasons.Should().NotBeEmpty();
    }

    [Test]
    public async Task GenerateChatResponseAsync_ProvidesRelevantGuidance()
    {
        var vpnResponse = await this.provider.GenerateChatResponseAsync("Tell me about the VPN kill switch");
        vpnResponse.Should().Contain("Kill Switch");

        var ratioResponse = await this.provider.GenerateChatResponseAsync("How does ratio work?");
        ratioResponse.Should().Contain("Ratio");

        var servarrResponse = await this.provider.GenerateChatResponseAsync("How do I connect Sonarr?");
        servarrResponse.Should().Contain("qBittorrent");
    }
}
