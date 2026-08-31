using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaEnrichmentServiceTest
{
    private ITorrentMediaMetadataRepository _repository = null!;
    private IMediaContainerInspector _inspector = null!;
    private IConfigService _configService = null!;
    private IAppFolderInfo _appFolderInfo = null!;
    private IEventAggregator _eventAggregator = null!;
    private MediaEnrichmentService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ITorrentMediaMetadataRepository>();
        _inspector = Substitute.For<IMediaContainerInspector>();
        _configService = Substitute.For<IConfigService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _appFolderInfo.AppDataFolder.Returns("/tmp/leecharr-app-data");
        _configService.AutoPruneRemovedArtwork.Returns(true);

        _service = new MediaEnrichmentService(
            _repository,
            _inspector,
            _configService,
            _appFolderInfo,
            _eventAggregator);
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenTorrentNull_ReturnsNull()
    {
        var result = await _service.EnrichTorrentAsync(null!);
        result.Should().BeNull();
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenNewTorrent_InsertsMetadataAndPublishesEvent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Severance.S02E01.2160p.ATVP.WEB-DL.DDP5.1.Atmos.DV.H.265-FLUX",
            Category = "tv"
        };

        _repository.GetByTorrentId(1).Returns((TorrentMediaMetadata)null!);

        var result = await _service.EnrichTorrentAsync(torrent);

        result.Should().NotBeNull();
        result.TorrentId.Should().Be(1);
        result.ArrType.Should().Be("Sonarr");
        _repository.Received(1).Insert(Arg.Is<TorrentMediaMetadata>(m => m.TorrentId == 1));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e => e.TorrentId == 1));
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenMovieCategory_GuessesRadarr()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Oppenheimer.2023.2160p.UHD.BluRay.x265-FLUX",
            Category = "radarr-movies"
        };

        _repository.GetByTorrentId(2).Returns((TorrentMediaMetadata)null!);

        var result = await _service.EnrichTorrentAsync(torrent);

        result.Should().NotBeNull();
        result.ArrType.Should().Be("Radarr");
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenMusicCategory_GuessesLidarr()
    {
        var torrent = new Torrent
        {
            Id = 3,
            Name = "Daft.Punk-Discovery.2001.FLAC",
            Category = "music"
        };

        _repository.GetByTorrentId(3).Returns((TorrentMediaMetadata)null!);

        var result = await _service.EnrichTorrentAsync(torrent);

        result.Should().NotBeNull();
        result.ArrType.Should().Be("Lidarr");
    }

    [Test]
    public void GetMetadata_ReturnsRepositoryMetadata()
    {
        var meta = new TorrentMediaMetadata { Id = 10, TorrentId = 5, Title = "Dune" };
        _repository.GetByTorrentId(5).Returns(meta);

        var result = _service.GetMetadata(5);

        result.Should().NotBeNull();
        result.Title.Should().Be("Dune");
    }

    [Test]
    public void DeleteMetadata_CallsRepositoryDeleteByTorrentId()
    {
        var meta = new TorrentMediaMetadata { Id = 10, TorrentId = 5, Title = "Dune" };
        _repository.GetByTorrentId(5).Returns(meta);

        _service.DeleteMetadata(5);

        _repository.Received(1).DeleteByTorrentId(5);
    }
}
