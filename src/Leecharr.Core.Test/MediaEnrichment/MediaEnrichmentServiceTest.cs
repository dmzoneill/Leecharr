// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http;
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

    [Test]
    public async Task EnrichTorrentAsync_WhenNoFilePath_GuessesMediaInfoFromTorrentName()
    {
        var guessedInfo = new MediaContainerInfo
        {
            Resolution = "1080p",
            VideoCodec = "x264",
        };
        this.inspector.Inspect(Arg.Any<Stream>(), "Movie.Title.2024.1080p.x264").Returns(guessedInfo);

        var torrent = new Torrent
        {
            Id = 88,
            Name = "Movie.Title.2024.1080p.x264",
            Category = "movies",
        };

        this.repository.GetByTorrentId(88).Returns((TorrentMediaMetadata)null!);

        var result = await this.service.EnrichTorrentAsync(torrent);

        result.Should().NotBeNull();
        result.MediaInfoJson.Should().NotBeNullOrEmpty();
        result.MediaInfoJson.Should().Contain("1080p");
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_InspectsPrimaryMediaFile()
    {
        var mediaFile = Path.Combine(this.tempDirectory, "video.mkv");
        await File.WriteAllBytesAsync(mediaFile, new byte[32]);

        var fileRepo = Substitute.For<ITorrentFileRepository>();
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 5, Path = "sample.txt", Size = 100 },
            new() { Id = 2, TorrentId = 5, Path = "video.mkv", Size = 50000 },
        };
        fileRepo.GetByTorrentId(5).Returns(files);

        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            torrentFileRepository: fileRepo);

        var torrent = new Torrent
        {
            Id = 5,
            Name = "Test.Movie",
            SavePath = this.tempDirectory,
        };

        var containerInfo = new MediaContainerInfo { Resolution = "4K UHD (2160p)" };
        this.inspector.InspectFile(mediaFile).Returns(containerInfo);

        customService.Handle(new TorrentDownloadCompletedEvent(torrent));

        // Allow async task to run
        await Task.Delay(200);

        this.inspector.Received(1).InspectFile(mediaFile);
    }

    [Test]
    public async Task Handle_ArchiveExtractionCompletedEvent_InspectsExtractedMediaFile()
    {
        var extractDir = Path.Combine(this.tempDirectory, "extracted");
        Directory.CreateDirectory(extractDir);
        var mediaFile = Path.Combine(extractDir, "extracted_movie.mkv");
        await File.WriteAllBytesAsync(mediaFile, new byte[64]);

        var torrent = new Torrent
        {
            Id = 6,
            Name = "Archived.Release",
            SavePath = this.tempDirectory,
        };

        var containerInfo = new MediaContainerInfo { Resolution = "1080p Full HD" };
        this.inspector.InspectFile(mediaFile).Returns(containerInfo);

        this.service.Handle(new ArchiveExtractionCompletedEvent
        {
            Torrent = torrent,
            DestinationDirectory = extractDir,
        });

        // Allow async task to run
        await Task.Delay(200);

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

    [TestCase("Show.S1E01", "Show", "TV")]
    [TestCase("Show.1x05", "Show", "TV")]
    [TestCase("Severance.S02E01.1080p.WEB-DL", "Severance", "TV")]
    [TestCase("Game.of.Thrones.Season.1.1080p", "Game of Thrones", "TV")]
    public async Task TmdbProvider_FetchMetadataAsync_ClassifiesTvAndCleansEpisodicTags(
        string releaseName, string expectedTitle, string expectedType)
    {
        var provider = new TmdbMetadataProvider();
        var metadata = await provider.FetchMetadataAsync(releaseName);

        metadata.Should().NotBeNull();
        metadata!.Title.Should().Be(expectedTitle);
        metadata.MediaType.Should().Be(expectedType);
    }

    [Test]
    public async Task TmdbProvider_FetchMetadataAsync_WhenMovieContainsWordSeason_ClassifiedAsMovieAndCleaned()
    {
        var provider = new TmdbMetadataProvider();
        var metadata = await provider.FetchMetadataAsync("Season.of.the.Witch.2011.1080p");

        metadata.Should().NotBeNull();
        metadata!.Title.Should().Be("Season of the Witch");
        metadata.Year.Should().Be(2011);
        metadata.MediaType.Should().Be("Movie");
    }

    [TestCase("Show.S1E01", "Show")]
    [TestCase("Show.1x05", "Show")]
    [TestCase("Season.of.the.Witch.2011.1080p", "Season of the Witch")]
    [TestCase("Severance.S02E01.1080p.WEB-DL", "Severance")]
    [TestCase("Blade.Runner.2049.2017.1080p", "Blade Runner 2049")]
    [TestCase("2001.A.Space.Odyssey.1968.1080p", "2001 A Space Odyssey")]
    [TestCase("1917.2019.1080p", "1917")]
    [TestCase("1984.1956.720p", "1984")]
    [TestCase("2012.2009.1080p", "2012")]
    public void CleanTitle_StripsEpisodicTagsAndYears_AcrossProviders(string raw, string expected)
    {
        TmdbMetadataProvider.CleanTitle(raw).Should().Be(expected);
        ServarrSyncMetadataProvider.CleanTitle(raw).Should().Be(expected);
        LocalNfoMetadataProvider.CleanTitle(raw).Should().Be(expected);
    }

    #endregion

    #region Dynamic Metadata, Local Artwork, Servarr Auth & Cache Cleanup

    [Test]
    public async Task EnrichTorrentAsync_WhenDynamicMetadataServiceAvailable_QueriesAndAppliesMetadata()
    {
        var metadataService = Substitute.For<IMediaMetadataService>();
        var dynamicResult = new MediaMetadata
        {
            Title = "Severance",
            Year = 2022,
            Overview = "Mark leads a team of office workers whose memories have been surgically divided.",
            PosterUrl = "http://example.com/severance_poster.jpg",
            BackdropUrl = "http://example.com/severance_backdrop.jpg",
            Genres = "Drama, Sci-Fi, Thriller",
            Rating = 8.7,
            ImdbId = "tt11280740",
            TmdbId = "95396",
            TvdbId = "371980",
            MediaType = "TV",
        };

        metadataService.GetMetadataAsync("Severance.S02E01.1080p.WEB-DL.x265", Arg.Any<string>(), Arg.Any<int?>())
            .Returns(Task.FromResult(dynamicResult));

        var handler = new TestHttpMessageHandler();
        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            mediaMetadataService: metadataService,
            httpClient: new HttpClient(handler));

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Severance.S02E01.1080p.WEB-DL.x265",
            Category = "tv",
        };

        var result = await customService.EnrichTorrentAsync(torrent);

        result.Should().NotBeNull();
        result.Title.Should().Be("Severance");
        result.Year.Should().Be(2022);
        result.Overview.Should().Contain("surgically divided");
        result.PosterUrl.Should().Be("http://example.com/severance_poster.jpg");
        result.BackdropUrl.Should().Be("http://example.com/severance_backdrop.jpg");
        result.Rating.Should().Be(8.7);
        result.ImdbId.Should().Be("tt11280740");
        result.TmdbId.Should().Be("95396");
        result.TvdbId.Should().Be("371980");
        result.ArrType.Should().Be("TV");

        await metadataService.Received(1).GetMetadataAsync("Severance.S02E01.1080p.WEB-DL.x265", "tv", Arg.Any<int?>());
    }

    [Test]
    public async Task CacheArtworkAsync_WhenLocalFilePath_CopiesDirectlyViaFileCopy()
    {
        var localSource = Path.Combine(this.tempDirectory, "local_artwork.png");
        await File.WriteAllBytesAsync(localSource, new byte[] { 10, 20, 30, 40 });

        var cachedPath = await this.service.CacheArtworkAsync(localSource, 201, "poster");

        cachedPath.Should().NotBeNull();
        File.Exists(cachedPath).Should().BeTrue();
        cachedPath.Should().EndWith(".png");
        cachedPath.Should().Contain(Path.Combine("MediaCache", "201"));

        var cachedBytes = await File.ReadAllBytesAsync(cachedPath);
        cachedBytes.Should().Equal(new byte[] { 10, 20, 30, 40 });
    }

    [Test]
    public async Task CacheArtworkAsync_WhenServarrEndpoint_IncludesXApiKeyHeader()
    {
        var arrRepository = Substitute.For<IArrConnectionRepository>();
        var arrConn = new ArrConnectionDefinition
        {
            Id = 1,
            Url = "https://sonarr.example.com",
            ApiKey = "servarr-secret-key-xyz",
            ArrType = "Sonarr",
        };
        arrRepository.All().Returns(new[] { arrConn });

        var safeHttpClient = Substitute.For<ISafeHttpClientService>();
        var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        safeHttpClient.DownloadBytesAsync(
            Arg.Any<Uri>(),
            Arg.Any<long>(),
            Arg.Any<IDictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns(validJpeg);

        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            arrRepository: arrRepository,
            safeHttpClientService: safeHttpClient);

        var url = "https://sonarr.example.com/api/v3/mediacover/42/poster.jpg";
        var result = await customService.CacheArtworkAsync(url, 202, "poster");

        result.Should().NotBeNull();
        File.Exists(result).Should().BeTrue();

        await safeHttpClient.Received(1).DownloadBytesAsync(
            Arg.Is<Uri>(u => u.ToString() == url),
            Arg.Any<long>(),
            Arg.Is<IDictionary<string, string>>(h => h != null && h.ContainsKey("X-Api-Key") && h["X-Api-Key"] == "servarr-secret-key-xyz"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CacheArtworkAsync_WhenUntrustedExternalUrlContainsMediacover_DoesNotIncludeXApiKeyHeader()
    {
        var arrRepository = Substitute.For<IArrConnectionRepository>();
        var arrConn = new ArrConnectionDefinition
        {
            Id = 1,
            Url = "https://sonarr.example.com",
            ApiKey = "servarr-secret-key-xyz",
            ArrType = "Sonarr",
        };
        arrRepository.All().Returns(new[] { arrConn });

        var safeHttpClient = Substitute.For<ISafeHttpClientService>();
        var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        safeHttpClient.DownloadBytesAsync(
            Arg.Any<Uri>(),
            Arg.Any<long>(),
            Arg.Any<IDictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns(validJpeg);

        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            arrRepository: arrRepository,
            safeHttpClientService: safeHttpClient);

        var url = "https://attacker.com/mediacover/poster.jpg";
        var result = await customService.CacheArtworkAsync(url, 203, "poster");

        result.Should().NotBeNull();
        File.Exists(result).Should().BeTrue();

        await safeHttpClient.Received(1).DownloadBytesAsync(
            Arg.Is<Uri>(u => u.ToString() == url),
            Arg.Any<long>(),
            Arg.Is<IDictionary<string, string>>(h => h == null || !h.ContainsKey("X-Api-Key")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetServarrApiKey_WhenUrlDoesNotMatchArrConnection_ReturnsNull()
    {
        var arrRepository = Substitute.For<IArrConnectionRepository>();
        var arrConn = new ArrConnectionDefinition
        {
            Id = 1,
            Url = "https://sonarr.example.com",
            ApiKey = "servarr-secret-key-xyz",
            ArrType = "Sonarr",
        };
        arrRepository.All().Returns(new[] { arrConn });

        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            arrRepository: arrRepository);

        var result = customService.GetServarrApiKey("https://attacker.com/mediacover/poster.jpg");
        result.Should().BeNull();
    }

    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://127.0.0.1:8080/admin/secrets.json")]
    [TestCase("http://localhost:5000/keys")]
    [TestCase("http://10.0.0.1/private")]
    [TestCase("http://192.168.1.1/admin")]
    [TestCase("http://172.16.0.1/internal")]
    public async Task CacheArtworkAsync_WhenRemoteUrlIsSsrfTarget_IsRejectedAndNotCached(string ssrfUrl)
    {
        var result = await this.service.CacheArtworkAsync(ssrfUrl, 999, "poster");

        result.Should().BeNull();
        var cacheDir = Path.Combine(this.tempDirectory, "MediaCache", "999");
        if (Directory.Exists(cacheDir))
        {
            Directory.GetFiles(cacheDir).Should().BeEmpty();
        }
    }

    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com/image.jpg")]
    [TestCase("gopher://example.com/")]
    public async Task CacheArtworkAsync_WhenSchemeIsNotHttpOrHttps_IsRejectedAndNotCached(string badSchemeUrl)
    {
        var result = await this.service.CacheArtworkAsync(badSchemeUrl, 998, "poster");

        result.Should().BeNull();
        var cacheDir = Path.Combine(this.tempDirectory, "MediaCache", "998");
        if (Directory.Exists(cacheDir))
        {
            Directory.GetFiles(cacheDir).Should().BeEmpty();
        }
    }

    [Test]
    public async Task CacheArtworkAsync_WhenPayloadIsNotValidImage_IsRejectedAndNotCached()
    {
        var safeHttpClient = Substitute.For<ISafeHttpClientService>();
        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            safeHttpClientService: safeHttpClient);

        var nonImagePayloads = new[]
        {
            System.Text.Encoding.UTF8.GetBytes("<html><body>Error 404 Not Found</body></html>"),
            System.Text.Encoding.UTF8.GetBytes("{\"access_token\": \"secret_token_12345\"}"),
            System.Text.Encoding.UTF8.GetBytes("AWS_SECRET_ACCESS_KEY=AKIAIOSFODNN7EXAMPLE"),
            new byte[] { 1, 2, 3, 4 },
        };

        var torrentId = 888;
        foreach (var payload in nonImagePayloads)
        {
            safeHttpClient.DownloadBytesAsync(
                Arg.Any<Uri>(),
                Arg.Any<long>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
                .Returns(payload);

            var result = await customService.CacheArtworkAsync("https://example.com/artwork.jpg", torrentId++, "poster");

            result.Should().BeNull();
        }

        var cacheParent = Path.Combine(this.tempDirectory, "MediaCache");
        if (Directory.Exists(cacheParent))
        {
            for (var id = 888; id < torrentId; id++)
            {
                var dir = Path.Combine(cacheParent, id.ToString());
                if (Directory.Exists(dir))
                {
                    Directory.GetFiles(dir).Should().BeEmpty();
                }
            }
        }
    }

    [Test]
    public async Task CacheArtworkAsync_WhenPayloadIsValidImage_IsSafelyCached()
    {
        var safeHttpClient = Substitute.For<ISafeHttpClientService>();
        var customService = new MediaEnrichmentService(
            this.repository,
            this.inspector,
            this.configService,
            this.appFolderInfo,
            this.eventAggregator,
            safeHttpClientService: safeHttpClient);

        var validImages = new Dictionary<string, byte[]>
        {
            ["jpeg"] = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 },
            ["png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D },
            ["webp"] = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x20, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 },
            ["gif"] = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00 },
        };

        var torrentId = 777;
        foreach (var (format, imageBytes) in validImages)
        {
            safeHttpClient.DownloadBytesAsync(
                Arg.Any<Uri>(),
                Arg.Any<long>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
                .Returns(imageBytes);

            var result = await customService.CacheArtworkAsync($"https://example.com/image.{format}", torrentId, "poster");

            result.Should().NotBeNull();
            File.Exists(result).Should().BeTrue();
            (await File.ReadAllBytesAsync(result)).Should().Equal(imageBytes);
            torrentId++;
        }
    }

    [Test]
    public void CleanupTorrentCache_DeletesCacheFolderOnTorrentDeletion()
    {
        var cacheDir = Path.Combine(this.tempDirectory, "MediaCache", "303");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(Path.Combine(cacheDir, "poster.jpg"), "dummy data");

        Directory.Exists(cacheDir).Should().BeTrue();

        this.service.CleanupTorrentCache(303);

        Directory.Exists(cacheDir).Should().BeFalse();
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage CapturedRequest { get; private set; }

        public byte[] ResponseBytes { get; set; } = new byte[] { 1, 2, 3, 4 };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.CapturedRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(this.ResponseBytes),
            };
            return Task.FromResult(response);
        }
    }

    #endregion
}
