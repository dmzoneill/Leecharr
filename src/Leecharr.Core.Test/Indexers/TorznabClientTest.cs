// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class TorznabClientTest
{
    private TorznabClient client = null!;

    [SetUp]
    public void SetUp()
    {
        this.client = new TorznabClient();
    }

    #region Torznab XML Response Parsing & Freeleech Badge

    [Test]
    public void ParseTorznabFeedXml_ExtractsReleasesAndFreeleechAttribute()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
  <channel>
    <title>Torznab Indexer Feed</title>
    <item>
      <title>Dune.Part.Two.2024.2160p.UHD.HDR.TrueHD.Atmos.7.1-FLUX</title>
      <guid>https://indexer.local/details/12345</guid>
      <link>https://indexer.local/download/12345.torrent</link>
      <pubDate>Mon, 01 Jan 2024 12:00:00 GMT</pubDate>
      <category>Movies &gt; UHD</category>
      <enclosure url=""https://indexer.local/download/12345.torrent"" length=""45000000000"" type=""application/x-bittorrent"" />
      <torznab:attr name=""seeders"" value=""150""/>
      <torznab:attr name=""peers"" value=""25""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0""/>
      <torznab:attr name=""uploadvolumefactor"" value=""2.0""/>
      <torznab:attr name=""infohash"" value=""0123456789ABCDEF0123456789ABCDEF01234567""/>
      <torznab:attr name=""magneturl"" value=""magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567""/>
    </item>
    <item>
      <title>Severance.S02E01.1080p.WEB-DL.x265</title>
      <guid>https://indexer.local/details/12346</guid>
      <link>https://indexer.local/download/12346.torrent</link>
      <size>2500000000</size>
      <newznab:attr name=""seeders"" value=""50""/>
      <newznab:attr name=""leechers"" value=""5""/>
      <newznab:attr name=""downloadvolumefactor"" value=""1""/>
      <newznab:attr name=""category"" value=""5040""/>
    </item>
    <item>
      <title>Half.Leech.Release.2024.1080p</title>
      <guid>https://indexer.local/details/12347</guid>
      <link>https://indexer.local/download/12347.torrent</link>
      <size>1000000000</size>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""peers"" value=""2""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0.5""/>
    </item>
  </channel>
</rss>";

        var indexer = new IndexerDefinition { Id = 1, Name = "TrackerAlpha", FreeleechOnly = false, MinSeeders = 1 };
        var results = this.client.ParseTorznabFeedXml(xml, indexer);

        results.Should().HaveCount(3);

        var first = results[0];
        first.Title.Should().Be("Dune.Part.Two.2024.2160p.UHD.HDR.TrueHD.Atmos.7.1-FLUX");
        first.Guid.Should().Be("https://indexer.local/details/12345");
        first.DownloadUrl.Should().Be("https://indexer.local/download/12345.torrent");
        first.Size.Should().Be(45000000000L);
        first.Seeders.Should().Be(150);
        first.Leechers.Should().Be(25);
        first.DownloadVolumeFactor.Should().Be(0.0);
        first.UploadVolumeFactor.Should().Be(2.0);
        first.IsFreeleech.Should().BeTrue();
        first.InfoHash.Should().Be("0123456789ABCDEF0123456789ABCDEF01234567");
        first.MagnetUrl.Should().StartWith("magnet:?");
        first.Category.Should().Be("Movies > UHD");
        first.IndexerName.Should().Be("TrackerAlpha");
        first.IndexerId.Should().Be(1);

        var second = results[1];
        second.Title.Should().Be("Severance.S02E01.1080p.WEB-DL.x265");
        second.Size.Should().Be(2500000000L);
        second.Seeders.Should().Be(50);
        second.Leechers.Should().Be(5);
        second.DownloadVolumeFactor.Should().Be(1.0);
        second.IsFreeleech.Should().BeFalse();
        second.Category.Should().Be("5040");

        var third = results[2];
        third.Title.Should().Be("Half.Leech.Release.2024.1080p");
        third.DownloadVolumeFactor.Should().Be(0.5);
        third.IsFreeleech.Should().BeFalse();
    }

    [Test]
    public void ParseTorznabFeedXml_WhenFreeleechOnly_FiltersOutNonFreeleech()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Release 1 (Freeleech)</title>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0""/>
    </item>
    <item>
      <title>Release 2 (Normal)</title>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""downloadvolumefactor"" value=""1""/>
    </item>
    <item>
      <title>Release 3 (50% Leech)</title>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0.5""/>
    </item>
  </channel>
</rss>";

        var indexer = new IndexerDefinition { Name = "TrackerBeta", FreeleechOnly = true, MinSeeders = 1 };
        var results = this.client.ParseTorznabFeedXml(xml, indexer);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Release 1 (Freeleech)");
        results[0].IsFreeleech.Should().BeTrue();
    }

    [Test]
    public void ParseTorznabFeedXml_WhenMinSeedersConfigured_FiltersOutReleasesBelowThreshold()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>High Seeds</title>
      <torznab:attr name=""seeders"" value=""50""/>
    </item>
    <item>
      <title>Low Seeds</title>
      <torznab:attr name=""seeders"" value=""3""/>
    </item>
  </channel>
</rss>";

        var indexer = new IndexerDefinition { Name = "TrackerGamma", MinSeeders = 10 };
        var results = this.client.ParseTorznabFeedXml(xml, indexer);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("High Seeds");
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("   ")]
    [TestCase("<invalid xml>>>>>")]
    [TestCase("<rss><somethingElse /></rss>")]
    public void ParseTorznabFeedXml_WhenInvalidOrEmptyXml_ReturnsEmptyList(string invalidXml)
    {
        var indexer = new IndexerDefinition { Name = "TrackerDelta" };
        var results = this.client.ParseTorznabFeedXml(invalidXml, indexer);

        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [TestCase("1")]
    [TestCase("true")]
    [TestCase("TRUE")]
    public void ParseTorznabFeedXml_WhenFreeleechAttributePresent_SetsDownloadVolumeFactorZero(string freeleechValue)
    {
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Freeleech Item</title>
      <torznab:attr name=""freeleech"" value=""{freeleechValue}""/>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());
        results.Should().HaveCount(1);
        results[0].DownloadVolumeFactor.Should().Be(0.0);
        results[0].IsFreeleech.Should().BeTrue();
    }

    [Test]
    public void ParseTorznabFeedXml_WhenIntegerFieldsWrappedInWhitespaceOrCData_ParsesCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Item with CDATA and whitespace</title>
      <size><![CDATA[ 1048576 ]]></size>
      <torznab:attr name=""seeders"" value=""
        42
      ""/>
      <torznab:attr name=""peers"" value=""  15  ""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());
        results.Should().HaveCount(1);
        results[0].Size.Should().Be(1048576L);
        results[0].Seeders.Should().Be(42);
        results[0].Leechers.Should().Be(15);
    }

    [Test]
    public void ParseTorznabFeedXml_WhenXmlContainsInvalidControlCharacters_SanitizesAndParsesSuccessfully()
    {
        var badChars = "\x01\x02\x08\x0B\x0C\x1F";
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Item{badChars} With Bad Control Chars</title>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());
        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Item With Bad Control Chars");
    }

    [Test]
    public void ParseTorznabFeedXml_WhenXmlContainsUnescapedAmpersand_ParsesSuccessfully()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Tom & Jerry The Movie 2021</title>
      <torznab:attr name=""seeders"" value=""25""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());
        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Tom & Jerry The Movie 2021");
    }

    #endregion
}
