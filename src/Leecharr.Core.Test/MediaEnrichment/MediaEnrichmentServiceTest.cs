// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaEnrichmentServiceTest
{
    private ITorrentMediaMetadataRepository repository = null!;
    private IMediaContainerInspector inspector = null!;
    private IConfigService configService = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private IEventAggregator eventAggregator = null!;
    private MediaEnrichmentService service = null!;
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "leecharr_enrichment_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);

        this.repository = Substitute.For<ITorrentMediaMetadataRepository>();
        this.inspector = Substitute.For<IMediaContainerInspector>();
        this.configService = Substitute.For<IConfigService>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.appFolderInfo.AppDataFolder.Returns(this.tempDirectory);
        this.configService.AutoPruneRemovedArtwork.Returns(true);

        this.service = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Exact Title, Release Name Parsing and ArrType Matching

    [Test]
    public async Task EnrichTorrentAsync_WhenTorrentNull_ReturnsNull()
    {
        var result = await this.service.EnrichTorrentAsync(null!);
        result.Should().BeNull();
    }

    [TestCase("tv", "Severance.S02E01.2160p.ATVP.WEB-DL", "Sonarr")]
    [TestCase("sonarr-shows", "The.Bear.S03E01.1080p.WEB-DL", "Sonarr")]
    [TestCase("shows", "Game.of.Thrones.Season.1.1080p", "Sonarr")]
    [TestCase("series", "Chernobyl.Season.1.Complete", "Sonarr")]
    [TestCase("radarr-movies", "Oppenheimer.2023.2160p.UHD.BluRay", "Radarr")]
    [TestCase("movies", "Dune.Part.Two.2024.1080p.Remux", "Radarr")]
    [TestCase("films", "The.Godfather.1972.2160p", "Radarr")]
    [TestCase("music", "Pink.Floyd-The.Dark.Side.Of.The.Moon.1973.FLAC", "Lidarr")]
    [TestCase("lidarr-albums", "Daft.Punk-Discovery.2001.FLAC", "Lidarr")]
    [TestCase("albums", "Radiohead-OK.Computer.1997.MP3", "Lidarr")]
    [TestCase("books", "Stephen.King-The.Shining.EPUB", "Readarr")]
    [TestCase("readarr-ebooks", "Frank.Herbert-Dune.MOBI", "Readarr")]
    [TestCase("other", "Random.Archive.Release.zip", "Unknown")]
    [TestCase("", "NonDescriptFile.iso", "Unknown")]
    public async Task EnrichTorrentAsync_ArrTypeClassification_CorrectlyMapsToServarrApp(
        string category, string name, string expectedArrType)
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = name,
            Category = category,
        };

        this.repository.GetByTorrentId(42).Returns((TorrentMediaMetadata)null!);

        var result = await this.service.EnrichTorrentAsync(torrent);

        result.Should().NotBeNull();
        result.TorrentId.Should().Be(42);
        result.Title.Should().Be(name);
        result.ArrType.Should().Be(expectedArrType);
        this.repository.Received(1).Insert(result);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e => e.TorrentId == 42));
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenMetadataAlreadyExists_UpdatesExistingRecord()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Existing.Movie.2024.1080p",
            Category = "movies",
        };

        var existing = new TorrentMediaMetadata
        {
            Id = 5,
            TorrentId = 10,
            Title = "Existing Movie",
        };

        this.repository.GetByTorrentId(10).Returns(existing);

        var result = await this.service.EnrichTorrentAsync(torrent);

        result.Should().BeSameAs(existing);
        this.repository.Received(1).Update(existing);
        this.repository.DidNotReceive().Insert(Arg.Any<TorrentMediaMetadata>());
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e => e.TorrentId == 10));
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenLocalFilePathProvidedAndExists_InspectsFileAndSerializesJson()
    {
        var mediaFile = Path.Combine(this.tempDirectory, "sample_movie.mkv");
        await File.WriteAllBytesAsync(mediaFile, new byte[16]);

        var containerInfo = new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            VideoCodec = "HEVC",
            Resolution = "4K UHD (2160p)",
            HdrFormat = "Dolby Vision",
        };

        this.inspector.InspectFile(mediaFile).Returns(containerInfo);

        var torrent = new Torrent
        {
            Id = 7,
            Name = "Sample.Movie.2024.2160p",
            Category = "movies",
        };

        this.repository.GetByTorrentId(7).Returns((TorrentMediaMetadata)null!);

        var result = await this.service.EnrichTorrentAsync(torrent, mediaFile);

        result.Should().NotBeNull();
        result.MediaInfoJson.Should().NotBeNullOrEmpty();
        result.MediaInfoJson.Should().Contain("Matroska (MKV)");
        result.MediaInfoJson.Should().Contain("Dolby Vision");
        this.inspector.Received(1).InspectFile(mediaFile);
    }

    #endregion

    #region Poster, Fanart, and Metadata Caching and Cleanup

    [Test]
    public void GetMetadata_ReturnsMetadataFromRepository()
    {
        var meta = new TorrentMediaMetadata { Id = 1, TorrentId = 99, Title = "Interstellar" };
        this.repository.GetByTorrentId(99).Returns(meta);

        var result = this.service.GetMetadata(99);

        result.Should().BeSameAs(meta);
        result.Title.Should().Be("Interstellar");
    }

    [Test]
    public void DeleteMetadata_WhenAutoPruneEnabled_DeletesLocalArtworkFilesAndDbRecord()
    {
        this.configService.AutoPruneRemovedArtwork.Returns(true);

        var posterFile = Path.Combine(this.tempDirectory, "test_poster.jpg");
        var backdropFile = Path.Combine(this.tempDirectory, "test_backdrop.jpg");
        File.WriteAllText(posterFile, "fake poster data");
        File.WriteAllText(backdropFile, "fake backdrop data");

        var meta = new TorrentMediaMetadata
        {
            Id = 1,
            TorrentId = 55,
            PosterLocalPath = posterFile,
            BackdropLocalPath = backdropFile,
        };

        this.repository.GetByTorrentId(55).Returns(meta);

        this.service.DeleteMetadata(55);

        File.Exists(posterFile).Should().BeFalse();
        File.Exists(backdropFile).Should().BeFalse();
        this.repository.Received(1).DeleteByTorrentId(55);
    }

    [Test]
    public void DeleteMetadata_WhenAutoPruneDisabled_PreservesArtworkFilesAndDeletesDbRecord()
    {
        this.configService.AutoPruneRemovedArtwork.Returns(false);

        var posterFile = Path.Combine(this.tempDirectory, "kept_poster.jpg");
        var backdropFile = Path.Combine(this.tempDirectory, "kept_backdrop.jpg");
        File.WriteAllText(posterFile, "fake poster data");
        File.WriteAllText(backdropFile, "fake backdrop data");

        var meta = new TorrentMediaMetadata
        {
            Id = 2,
            TorrentId = 56,
            PosterLocalPath = posterFile,
            BackdropLocalPath = backdropFile,
        };

        this.repository.GetByTorrentId(56).Returns(meta);

        this.service.DeleteMetadata(56);

        File.Exists(posterFile).Should().BeTrue();
        File.Exists(backdropFile).Should().BeTrue();
        this.repository.Received(1).DeleteByTorrentId(56);
    }

    [Test]
    public void DeleteMetadata_WhenMetadataNotFound_DoesNotThrow()
    {
        this.repository.GetByTorrentId(999).Returns((TorrentMediaMetadata)null!);

        var act = () => this.service.DeleteMetadata(999);

        act.Should().NotThrow();
        this.repository.DidNotReceive().DeleteByTorrentId(Arg.Any<int>());
    }

    #endregion

    #region Local NFO Provider Integration Tests

    [Test]
    public async Task LocalNfoProvider_ParsesMovieNfoXmlSuccessfully()
    {
        var nfoFolder = Path.Combine(this.tempDirectory, "MovieFolder");
        Directory.CreateDirectory(nfoFolder);

        var nfoPath = Path.Combine(nfoFolder, "movie.nfo");
        var xmlContent = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<movie>
    <title>Dune: Part Two</title>
    <year>2024</year>
    <plot>Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.</plot>
    <rating>8.6</rating>
    <id>tt15239678</id>
    <tmdbid>693134</tmdbid>
</movie>";
        await File.WriteAllTextAsync(nfoPath, xmlContent);

        // Also place a poster.jpg
        var posterPath = Path.Combine(nfoFolder, "poster.jpg");
        await File.WriteAllTextAsync(posterPath, "fake image");

        var provider = new LocalNfoMetadataProvider();
        var metadata = await provider.FetchMetadataAsync(nfoPath, "movies", 2024);

        metadata.Should().NotBeNull();
        metadata.Title.Should().Be("Dune: Part Two");
        metadata.Year.Should().Be(2024);
        metadata.Overview.Should().Contain("Paul Atreides unites with Chani");
        metadata.Rating.Should().Be(8.6);
        metadata.ImdbId.Should().Be("tt15239678");
        metadata.TmdbId.Should().Be("693134");
        metadata.MediaType.Should().Be("Movie");
        metadata.PosterUrl.Should().Be(posterPath);
    }

    [Test]
    public async Task LocalNfoProvider_ParsesTvShowNfoXmlSuccessfully()
    {
        var tvFolder = Path.Combine(this.tempDirectory, "TvFolder");
        Directory.CreateDirectory(tvFolder);

        var nfoPath = Path.Combine(tvFolder, "tvshow.nfo");
        var xmlContent = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<tvshow>
    <title>Severance</title>
    <year>2022</year>
    <outline>Mark leads a team of office workers whose memories have been surgically divided between their work and personal lives.</outline>
    <rating>8.7</rating>
</tvshow>";
        await File.WriteAllTextAsync(nfoPath, xmlContent);

        var fanartPath = Path.Combine(tvFolder, "fanart.jpg");
        await File.WriteAllTextAsync(fanartPath, "fake fanart");

        var provider = new LocalNfoMetadataProvider();
        var metadata = await provider.FetchMetadataAsync(nfoPath, "tv");

        metadata.Should().NotBeNull();
        metadata.Title.Should().Be("Severance");
        metadata.Year.Should().Be(2022);
        metadata.Overview.Should().Contain("Mark leads a team of office workers");
        metadata.Rating.Should().Be(8.7);
        metadata.MediaType.Should().Be("TV");
        metadata.BackdropUrl.Should().Be(fanartPath);
    }

    [Test]
    public async Task LocalNfoProvider_WhenXmlMalformed_FallsBackToRegexExtraction()
    {
        var malformedFolder = Path.Combine(this.tempDirectory, "MalformedFolder");
        Directory.CreateDirectory(malformedFolder);

        var nfoPath = Path.Combine(malformedFolder, "info.nfo");
        var rawText = @"Some Scene Header Text
<title>Oppenheimer</title>
<year>2023</year>
<plot>The story of American scientist J. Robert Oppenheimer.</plot>
<rating>8.9</rating>
Unclosed tags and arbitrary scene ascii art <<<<< ===== >>>>>";
        await File.WriteAllTextAsync(nfoPath, rawText);

        var provider = new LocalNfoMetadataProvider();
        var metadata = await provider.FetchMetadataAsync(nfoPath, "movies");

        metadata.Should().NotBeNull();
        metadata.Title.Should().Be("Oppenheimer");
        metadata.Year.Should().Be(2023);
        metadata.Overview.Should().Be("The story of American scientist J. Robert Oppenheimer.");
        metadata.Rating.Should().Be(8.9);
    }

    [Test]
    public async Task LocalNfoProvider_ProbeHealthAsync_ReturnsHealthy()
    {
        var provider = new LocalNfoMetadataProvider();
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
    }

    #endregion

    #region TMDB & TVDB Provider Integration Tests

    [Test]
    public async Task TmdbProvider_FetchMetadataAsync_CleansTitleAndGeneratesFallbackMetadata()
    {
        var provider = new TmdbMetadataProvider();
        var metadata = await provider.FetchMetadataAsync("Oppenheimer.2023.2160p.UHD.BluRay.x265-FLUX", "movies");

        metadata.Should().NotBeNull();
        metadata.Title.Should().Be("Oppenheimer");
        metadata.Year.Should().Be(2023);
        metadata.MediaType.Should().Be("Movie");
    }

    [Test]
    public async Task TmdbProvider_ProbeHealthAsync_ReturnsHealthy()
    {
        var provider = new TmdbMetadataProvider();
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
    }

    [Test]
    public async Task TvdbProvider_FetchMetadataAsync_CleansTvTitleAndExtractsMetadata()
    {
        var provider = new TvdbMetadataProvider();
        var metadata = await provider.FetchMetadataAsync("Severance.S02E01.1080p.WEB-DL.x265", "tv", 2022);

        metadata.Should().NotBeNull();
        metadata.Title.Should().Be("Severance");
        metadata.Year.Should().Be(2022);
        metadata.MediaType.Should().Be("TV");
    }

    [Test]
    public async Task TvdbProvider_ProbeHealthAsync_ReturnsHealthy()
    {
        var provider = new TvdbMetadataProvider();
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
    }

    #endregion
}
