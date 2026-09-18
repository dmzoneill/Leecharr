// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentFileServiceTest
{
    private ITorrentFileRepository repository = null!;
    private IDownloadEngine downloadEngine = null!;
    private IEventAggregator eventAggregator = null!;
    private TorrentFileService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<ITorrentFileRepository>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.service = new TorrentFileService(
            this.repository,
            this.downloadEngine,
            this.eventAggregator);
    }

    [Test]
    public async Task SetPriorityAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        this.repository.Get(99).Returns((TorrentFile)null!);

        var result = await this.service.SetPriorityAsync(1, 99, 4);

        result.Should().BeFalse();
        this.repository.DidNotReceive().Update(Arg.Any<TorrentFile>());
        await this.downloadEngine.DidNotReceiveWithAnyArgs().SetFilePriorityAsync(default, default!, default);
    }

    [Test]
    public async Task SetPriorityAsync_WhenFileBelongsToDifferentTorrent_ReturnsFalse()
    {
        var file = new TorrentFile { Id = 10, TorrentId = 2, Path = "movie.mp4", Priority = 3 };
        this.repository.Get(10).Returns(file);

        var result = await this.service.SetPriorityAsync(1, 10, 4);

        result.Should().BeFalse();
        this.repository.DidNotReceive().Update(Arg.Any<TorrentFile>());
        await this.downloadEngine.DidNotReceiveWithAnyArgs().SetFilePriorityAsync(default, default!, default);
    }

    [Test]
    public async Task SetPriorityAsync_WhenFileExistsAndBelongsToTorrent_UpdatesPriorityAndCallsEngine()
    {
        var file = new TorrentFile { Id = 10, TorrentId = 1, Path = "movie.mp4", Priority = 3 };
        this.repository.Get(10).Returns(file);

        var result = await this.service.SetPriorityAsync(1, 10, 4);

        result.Should().BeTrue();
        file.Priority.Should().Be(4);
        this.repository.Received(1).Update(file);
        await this.downloadEngine.Received(1).SetFilePriorityAsync(1, "movie.mp4", 4);
    }

    [Test]
    public async Task SetPriorityAsync_WhenPriorityOutOfRange_ClampsBetween0And7()
    {
        var file = new TorrentFile { Id = 10, TorrentId = 1, Path = "movie.mp4", Priority = 3 };
        this.repository.Get(10).Returns(file);

        var result = await this.service.SetPriorityAsync(1, 10, 10);

        result.Should().BeTrue();
        file.Priority.Should().Be(7);
        this.repository.Received(1).Update(file);
        await this.downloadEngine.Received(1).SetFilePriorityAsync(1, "movie.mp4", 7);
    }

    [Test]
    public async Task SetPrioritiesAsync_BatchUpdatesAllFiles()
    {
        var file1 = new TorrentFile { Id = 10, TorrentId = 1, Path = "f1.txt", Priority = 3 };
        var file2 = new TorrentFile { Id = 11, TorrentId = 1, Path = "f2.txt", Priority = 3 };
        this.repository.Get(10).Returns(file1);
        this.repository.Get(11).Returns(file2);

        var priorities = new List<(int FileId, int Priority)>
        {
            (10, 0),
            (11, 5),
        };

        var result = await this.service.SetPrioritiesAsync(1, priorities);

        result.Should().BeTrue();
        file1.Priority.Should().Be(0);
        file2.Priority.Should().Be(5);
        this.repository.Received(1).Update(file1);
        this.repository.Received(1).Update(file2);
        await this.downloadEngine.Received(1).SetFilePriorityAsync(1, "f1.txt", 0);
        await this.downloadEngine.Received(1).SetFilePriorityAsync(1, "f2.txt", 5);
    }

    [Test]
    public async Task SetPrioritiesAsync_WhenAnyFileFails_ReturnsFalse()
    {
        var file1 = new TorrentFile { Id = 10, TorrentId = 1, Path = "f1.txt", Priority = 3 };
        this.repository.Get(10).Returns(file1);
        this.repository.Get(99).Returns((TorrentFile)null!);

        var priorities = new List<(int FileId, int Priority)>
        {
            (10, 0),
            (99, 5),
        };

        var result = await this.service.SetPrioritiesAsync(1, priorities);

        result.Should().BeFalse();
        file1.Priority.Should().Be(0);
        this.repository.Received(1).Update(file1);
    }
}
