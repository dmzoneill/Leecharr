// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ArchiveExtractionAndTorznabFeedIntegrationTests : IntegrationTestBase
{
    private string workDir = null!;

    [SetUp]
    public void SetUp()
    {
        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        this.workDir = Path.Combine(baseDir, "leecharr_extract_integ_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.workDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.workDir))
        {
            try
            {
                Directory.Delete(this.workDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task ArchiveExtractor_ZipFile_ExtractsSuccessfully()
    {
        var diskProvider = GlobalSetup.Factory.Services.GetRequiredService<IDiskProvider>();
        var extractor = new ArchiveExtractorService(diskProvider);

        // 1. Create a zip archive with sample files
        var zipPath = Path.Combine(this.workDir, "sample_archive.zip");
        var extractDest = Path.Combine(this.workDir, "extracted");
        Directory.CreateDirectory(extractDest);

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("movie_data.txt");
            using (var writer = new StreamWriter(entry1.Open()))
            {
                await writer.WriteAsync("Sample video stream payload text");
            }

            var entry2 = archive.CreateEntry("subfolder/sample.nfo");
            using (var writer = new StreamWriter(entry2.Open()))
            {
                await writer.WriteAsync("Release info and metadata");
            }
        }

        // 2. Verify file is detected as archive
        extractor.IsArchiveFile(zipPath).Should().BeTrue();
        extractor.IsArchiveFile("movie.mp4").Should().BeFalse();

        // 3. Extract archive
        var result = await extractor.ExtractArchiveAsync(zipPath, extractDest);
        result.Should().BeTrue();

        // 4. Verify extracted files exist
        File.Exists(Path.Combine(extractDest, "movie_data.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDest, "subfolder", "sample.nfo")).Should().BeTrue();
    }

    [Test]
    public void TorznabClient_ParseFeedXml_ExtractsAttributesAndEnclosures()
    {
        var torznabClient = GlobalSetup.Factory.Services.GetRequiredService<ITorznabClient>();

        const string sampleRss = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Sample Torznab Tracker</title>
        <item>
            <title>Inception.2010.1080p.BluRay.x264</title>
            <guid>https://tracker.example.com/details/101</guid>
            <pubDate>Mon, 01 Jan 2026 12:00:00 +0000</pubDate>
            <size>4294967296</size>
            <comments>https://tracker.example.com/comments/101</comments>
            <enclosure url=""https://tracker.example.com/download/101.torrent"" length=""4294967296"" type=""application/x-bittorrent"" />
            <torznab:attr name=""category"" value=""2000"" />
            <torznab:attr name=""category"" value=""2040"" />
            <torznab:attr name=""seeders"" value=""50"" />
            <torznab:attr name=""peers"" value=""10"" />
            <torznab:attr name=""infohash"" value=""4444555566667777888899990000111122223333"" />
            <torznab:attr name=""magneturl"" value=""magnet:?xt=urn:btih:4444555566667777888899990000111122223333&amp;dn=Inception"" />
            <torznab:attr name=""downloadvolumefactor"" value=""0"" />
            <torznab:attr name=""uploadvolumefactor"" value=""1"" />
            <torznab:attr name=""minimumratio"" value=""1.0"" />
            <torznab:attr name=""minimumseedtime"" value=""172800"" />
            <torznab:attr name=""imdbid"" value=""1375666"" />
            <torznab:attr name=""tmdbid"" value=""27205"" />
        </item>
    </channel>
</rss>";

        var results = torznabClient.ParseTorznabFeedXml(sampleRss);
        results.Should().NotBeNull();
        results.Should().HaveCount(1);

        var first = results[0];
        first.Title.Should().Be("Inception.2010.1080p.BluRay.x264");
        first.Size.Should().Be(4294967296);
        first.Seeders.Should().Be(50);
        first.Peers.Should().BeGreaterThan(0);
        first.InfoHash.Should().Be("4444555566667777888899990000111122223333");
        first.DownloadUrl.Should().Be("https://tracker.example.com/download/101.torrent");
        first.MagnetUrl.Should().Contain("4444555566667777888899990000111122223333");
        first.IsFreeleech.Should().BeTrue();
        first.DownloadVolumeFactor.Should().Be(0.0);
    }

    [Test]
    public void TorznabClient_ParseCapabilitiesXml_ExtractsCategoriesAndSearchTypes()
    {
        var torznabClient = GlobalSetup.Factory.Services.GetRequiredService<ITorznabClient>();

        const string capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <server version=""1.0"" title=""Sample Tracker"" />
    <limits max=""100"" default=""50"" />
    <searching>
        <search available=""yes"" supportedParams=""q"" />
        <tv-search available=""yes"" supportedParams=""q,season,ep"" />
        <movie-search available=""yes"" supportedParams=""q,imdbid,tmdbid"" />
        <music-search available=""yes"" supportedParams=""q,artist,album"" />
        <book-search available=""no"" supportedParams=""q"" />
    </searching>
    <categories>
        <category id=""2000"" name=""Movies"">
            <subcat id=""2040"" name=""Movies/HD"" />
            <subcat id=""2045"" name=""Movies/UHD"" />
        </category>
        <category id=""5000"" name=""TV"">
            <subcat id=""5040"" name=""TV/HD"" />
        </category>
    </categories>
</caps>";

        var caps = torznabClient.ParseCapabilitiesXml(capsXml);
        caps.Should().NotBeNull();
        caps.Categories.Should().NotBeEmpty();
        caps.SupportsSearch.Should().BeTrue();
        caps.SupportsTvSearch.Should().BeTrue();
        caps.SupportsMovieSearch.Should().BeTrue();
        caps.SupportsMusicSearch.Should().BeTrue();
        caps.SupportsBookSearch.Should().BeFalse();
    }
}
