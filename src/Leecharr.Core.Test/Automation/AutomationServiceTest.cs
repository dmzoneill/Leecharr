using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Automation;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationServiceTest
{
    private IAutomationScriptRepository _scriptRepository;
    private ITorrentRepository _torrentRepository;
    private ITagRepository _tagRepository;
    private IEventAggregator _eventAggregator;
    private IDownloadEngine _downloadEngine;
    private IBlocklistService _blocklistService;
    private ITrackerBoostService _trackerBoostService;
    private IExtractorService _extractorService;
    private IDiskProvider _diskProvider;

    [SetUp]
    public void SetUp()
    {
        _scriptRepository = Substitute.For<IAutomationScriptRepository>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _tagRepository = Substitute.For<ITagRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _downloadEngine = Substitute.For<IDownloadEngine>();
        _blocklistService = Substitute.For<IBlocklistService>();
        _trackerBoostService = Substitute.For<ITrackerBoostService>();
        _extractorService = Substitute.For<IExtractorService>();
        _diskProvider = Substitute.For<IDiskProvider>();
    }

    [Test]
    public void ShouldBanPeer_WhenScriptCallsBanPeer()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "TestTorrent",
            SavePath = "/downloads/TestTorrent",
        };

        var script = new AutomationScript
        {
            Id = 1,
            Name = "BanPeerScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.banPeer('198.51.100.42');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.PeersToBan.Should().Contain("198.51.100.42");
        _blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r.Contains("198.51.100.42")));
    }

    [Test]
    public void ShouldBoostTracker_WhenScriptCallsBoostTracker()
    {
        var torrent = new Torrent
        {
            Id = 20,
            Name = "BoostTorrent",
            SavePath = "/downloads/BoostTorrent",
        };

        var script = new AutomationScript
        {
            Id = 2,
            Name = "BoostScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.boostTracker();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldBoostTracker.Should().BeTrue();
        _trackerBoostService.Received(1).BoostTorrentAsync(20);
    }

    [Test]
    public void ShouldBoostTracker_WhenTrackerBoostServiceNull_FallsBackToDownloadEngine()
    {
        var torrent = new Torrent
        {
            Id = 25,
            Name = "FallbackTorrent",
            SavePath = "/downloads/FallbackTorrent",
        };

        var script = new AutomationScript
        {
            Id = 3,
            Name = "BoostScriptFallback",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.boostTracker();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: null,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldBoostTracker.Should().BeTrue();
        _downloadEngine.Received(1).ForceAnnounceAsync(25);
    }

    [Test]
    public void ShouldExtractArchive_WhenScriptCallsExtractArchive()
    {
        var torrent = new Torrent
        {
            Id = 30,
            Name = "ArchiveTorrent",
            SavePath = "/downloads/ArchiveTorrent",
        };

        var script = new AutomationScript
        {
            Id = 4,
            Name = "ExtractScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.extractArchive('/custom/extracted', true);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldExtractArchive.Should().BeTrue();
        result.ExtractDestination.Should().Be("/custom/extracted");
        result.DeleteArchiveOnExtract.Should().BeTrue();
        _extractorService.Received(1).ExtractAsync(torrent, "/custom/extracted", true);
    }

    [Test]
    public void ShouldCleanUnwantedFiles_WhenScriptCallsCleanUnwantedFiles()
    {
        var torrent = new Torrent
        {
            Id = 40,
            Name = "CleanTorrent",
            SavePath = "/downloads/CleanTorrent",
        };

        var script = new AutomationScript
        {
            Id = 5,
            Name = "CleanScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.cleanUnwantedFiles('*.nfo', '*.txt');",
        };

        _diskProvider.FolderExists(torrent.SavePath).Returns(true);
        _diskProvider.GetFiles(torrent.SavePath, true).Returns(new[]
        {
            "/downloads/CleanTorrent/movie.mkv",
            "/downloads/CleanTorrent/release.nfo",
            "/downloads/CleanTorrent/readme.txt",
        });

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.CleanFilePatterns.Should().Contain("*.nfo");
        result.CleanFilePatterns.Should().Contain("*.txt");

        _diskProvider.Received(1).DeleteFile("/downloads/CleanTorrent/release.nfo");
        _diskProvider.Received(1).DeleteFile("/downloads/CleanTorrent/readme.txt");
        _diskProvider.DidNotReceive().DeleteFile("/downloads/CleanTorrent/movie.mkv");
    }

    [Test]
    public void ShouldNotThrow_WhenOptionalDependenciesAreNull()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "NullDepTorrent",
            SavePath = "/downloads/NullDepTorrent",
        };

        var script = new AutomationScript
        {
            Id = 6,
            Name = "AllActionsScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = @"
                torrent.banPeer('1.2.3.4');
                torrent.boostTracker();
                torrent.extractArchive('/extracted', false);
                torrent.cleanUnwantedFiles('*.nfo');
            ",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var act = () => service.ExecuteScript(script, torrent);
        act.Should().NotThrow();
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_ExtractsAndDeletesArchivesAndSiblings_WhenDeleteArchiveIsTrue()
    {
        var torrent = new Torrent
        {
            Id = 100,
            Name = "MultipartTorrent",
            SavePath = "/downloads/MultipartTorrent",
        };

        var archiveExtractorService = Substitute.For<IArchiveExtractorService>();
        archiveExtractorService.IsArchiveFile(Arg.Any<string>()).Returns(call =>
        {
            var p = (string)call[0];
            return p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
        });
        archiveExtractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var files = new[]
        {
            "/downloads/MultipartTorrent/movie.part1.rar",
            "/downloads/MultipartTorrent/movie.part2.rar",
            "/downloads/MultipartTorrent/movie.nfo",
        };

        _diskProvider.FolderExists("/downloads/MultipartTorrent").Returns(true);
        _diskProvider.GetFiles("/downloads/MultipartTorrent", true).Returns(files);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(call => files.Contains((string)call[0]));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractorService);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, "/extracted", true);

        result.Should().BeTrue();
        await archiveExtractorService.Received(1).ExtractArchiveAsync("/downloads/MultipartTorrent/movie.part1.rar", "/extracted");
        await archiveExtractorService.DidNotReceive().ExtractArchiveAsync("/downloads/MultipartTorrent/movie.part2.rar", Arg.Any<string>());

        _diskProvider.Received(1).DeleteFile("/downloads/MultipartTorrent/movie.part1.rar");
        _diskProvider.Received(1).DeleteFile("/downloads/MultipartTorrent/movie.part2.rar");
        _diskProvider.DidNotReceive().DeleteFile("/downloads/MultipartTorrent/movie.nfo");
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_DoesNotDeleteArchives_WhenExtractionFails()
    {
        var torrent = new Torrent
        {
            Id = 101,
            Name = "FailedTorrent",
            SavePath = "/downloads/FailedTorrent",
        };

        var archiveExtractorService = Substitute.For<IArchiveExtractorService>();
        archiveExtractorService.IsArchiveFile(Arg.Any<string>()).Returns(call =>
        {
            var p = (string)call[0];
            return p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
        });
        archiveExtractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var files = new[]
        {
            "/downloads/FailedTorrent/corrupted.part1.rar",
            "/downloads/FailedTorrent/corrupted.part2.rar",
        };

        _diskProvider.FolderExists("/downloads/FailedTorrent").Returns(true);
        _diskProvider.GetFiles("/downloads/FailedTorrent", true).Returns(files);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(call => files.Contains((string)call[0]));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractorService);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, "/extracted", true);

        result.Should().BeFalse();
        _diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_DoesNotDeleteArchives_WhenDeleteArchiveIsFalse()
    {
        var torrent = new Torrent
        {
            Id = 102,
            Name = "KeepArchiveTorrent",
            SavePath = "/downloads/KeepArchiveTorrent",
        };

        var archiveExtractorService = Substitute.For<IArchiveExtractorService>();
        archiveExtractorService.IsArchiveFile(Arg.Any<string>()).Returns(call =>
        {
            var p = (string)call[0];
            return p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
        });
        archiveExtractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var files = new[]
        {
            "/downloads/KeepArchiveTorrent/movie.part1.rar",
            "/downloads/KeepArchiveTorrent/movie.part2.rar",
        };

        _diskProvider.FolderExists("/downloads/KeepArchiveTorrent").Returns(true);
        _diskProvider.GetFiles("/downloads/KeepArchiveTorrent", true).Returns(files);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(call => files.Contains((string)call[0]));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractorService);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, "/extracted", false);

        result.Should().BeTrue();
        _diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
    }
}
