// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
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
      <torznab:attr name=""peers"" value=""175""/>
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
        first.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
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

    [Test]
    public void ParseTorznabFeedXml_WhenItemContainsBothRssCategoryAndTorznabCategoryAttrs_CollectsAllCategories()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Movies.HD.Sample</title>
      <category>Movies/HD</category>
      <torznab:attr name=""category"" value=""2000""/>
      <torznab:attr name=""category"" value=""2040""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].Category.Should().Be("Movies/HD, 2000, 2040");
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
      <torznab:attr name=""leechers"" value=""  15  ""/>
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

    [Test]
    public void ParseTorznabFeedXml_WhenMagnetInDownloadUrlOrLinkAndMagnetUrlEmpty_PopulatesMagnetUrl()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Magnet Enclosure Release</title>
      <enclosure url=""magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01&amp;dn=Release1"" length=""1000"" type=""application/x-bittorrent"" />
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
    <item>
      <title>Magnet Link Release</title>
      <link>magnet:?xt=urn:btih:1234567890ABCDEF1234567890ABCDEF12345678&amp;dn=Release2</link>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());
        results.Should().HaveCount(2);
        results[0].MagnetUrl.Should().StartWith("magnet:?");
        results[1].MagnetUrl.Should().StartWith("magnet:?");
    }

    [Test]
    public void ParseTorznabFeedXml_WhenEnclosureUrlIsEmptyOrWhitespace_FallsBackToLinkUrl()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Empty.Enclosure.Url.Release.1080p</title>
      <guid>https://indexer.example.com/details/101</guid>
      <link>https://indexer.example.com/download/101.torrent</link>
      <enclosure url="""" length=""0"" type=""application/x-bittorrent"" />
      <torznab:attr name=""seeders"" value=""20""/>
    </item>
    <item>
      <title>Whitespace.Enclosure.Url.Release.1080p</title>
      <guid>https://indexer.example.com/details/102</guid>
      <link>https://indexer.example.com/download/102.torrent</link>
      <enclosure url=""   "" length=""1000"" type=""application/x-bittorrent"" />
      <torznab:attr name=""seeders"" value=""15""/>
    </item>
    <item>
      <title>Missing.Enclosure.Url.Attr.Release.1080p</title>
      <guid>https://indexer.example.com/details/103</guid>
      <link>https://indexer.example.com/download/103.torrent</link>
      <enclosure length=""2000"" type=""application/x-bittorrent"" />
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());

        results.Should().HaveCount(3);
        results[0].DownloadUrl.Should().Be("https://indexer.example.com/download/101.torrent");
        results[1].DownloadUrl.Should().Be("https://indexer.example.com/download/102.torrent");
        results[2].DownloadUrl.Should().Be("https://indexer.example.com/download/103.torrent");
    }

    [Test]
    public void ParseTorznabFeedXml_HtmlEntitiesInTitlesAndFields_DecodesCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Spider-Man:&amp;nbsp;No&amp;nbsp;Way&amp;nbsp;Home&amp;quot;2021&amp;quot;&amp;amp;Friends</title>
      <guid>https://indexer.local/details?id=123&amp;amp;passkey=xyz</guid>
      <link>https://indexer.local/download?id=123&amp;amp;auth=token</link>
      <description>4K&amp;nbsp;UHD&amp;nbsp;&amp;amp;&amp;nbsp;HDR10&amp;nbsp;Release</description>
      <comments>https://indexer.local/comments?id=123&amp;amp;view=all</comments>
      <category>&amp;lt;Movies&amp;gt;&amp;nbsp;&amp;amp;&amp;nbsp;TV</category>
      <torznab:attr name=""category"" value=""Movies&amp;nbsp;&amp;amp;&amp;nbsp;TV""/>
      <torznab:attr name=""seeders"" value=""50""/>
      <torznab:attr name=""leechers"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());

        results.Should().HaveCount(1);
        var release = results[0];
        release.Title.Should().Be("Spider-Man:\u00A0No\u00A0Way\u00A0Home\"2021\"&Friends");
        release.Guid.Should().Be("https://indexer.local/details?id=123&passkey=xyz");
        release.DownloadUrl.Should().Be("https://indexer.local/download?id=123&auth=token");
        release.Description.Should().Be("4K\u00A0UHD\u00A0&\u00A0HDR10\u00A0Release");
        release.Comments.Should().Be("https://indexer.local/comments?id=123&view=all");
        release.Category.Should().Contain("<Movies>\u00A0&\u00A0TV");
        release.Category.Should().Contain("Movies\u00A0&\u00A0TV");
    }

    [Test]
    public void ParseTorznabFeedXml_PeersVsLeechersCollision_CalculatesCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Explicit.Leechers.Release</title>
      <torznab:attr name=""seeders"" value=""40""/>
      <torznab:attr name=""peers"" value=""100""/>
      <torznab:attr name=""leechers"" value=""12""/>
    </item>
    <item>
      <title>Computed.Leechers.From.Peers.Release</title>
      <torznab:attr name=""seeders"" value=""40""/>
      <torznab:attr name=""peers"" value=""60""/>
    </item>
    <item>
      <title>Peers.LessThan.Seeders.Release</title>
      <torznab:attr name=""seeders"" value=""40""/>
      <torznab:attr name=""peers"" value=""10""/>
    </item>
    <item>
      <title>Only.Seeders.Release</title>
      <torznab:attr name=""seeders"" value=""40""/>
    </item>
    <item>
      <title>Peers.And.Leechers.Only.Release</title>
      <torznab:attr name=""peers"" value=""80""/>
      <torznab:attr name=""leechers"" value=""15""/>
    </item>
    <item>
      <title>Direct.Elements.Release</title>
      <seeders>75</seeders>
      <peers>100</peers>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());

        results.Should().HaveCount(6);
        // Item 1: Explicit leechers takes precedence over rawPeers
        results[0].Seeders.Should().Be(40);
        results[0].Leechers.Should().Be(12);
        results[0].Peers.Should().Be(100);

        // Item 2: rawPeers - seeders = 60 - 40 = 20
        results[1].Seeders.Should().Be(40);
        results[1].Leechers.Should().Be(20);
        results[1].Peers.Should().Be(60);

        // Item 3: rawPeers (10) < seeders (40) => Math.Max(0, 10 - 40) = 0
        results[2].Seeders.Should().Be(40);
        results[2].Leechers.Should().Be(0);
        results[2].Peers.Should().Be(40);

        // Item 4: Only seeders => leechers = 0, peers = 40
        results[3].Seeders.Should().Be(40);
        results[3].Leechers.Should().Be(0);
        results[3].Peers.Should().Be(40);

        // Item 5: Peers and leechers without explicit seeders => seeders = 80 - 15 = 65
        results[4].Seeders.Should().Be(65);
        results[4].Leechers.Should().Be(15);
        results[4].Peers.Should().Be(80);

        // Item 6: Direct XML elements => seeders = 75, leechers = 25, peers = 100
        results[5].Seeders.Should().Be(75);
        results[5].Leechers.Should().Be(25);
        results[5].Peers.Should().Be(100);
    }

    [Test]
    public void ParseTorznabFeedXml_AttributeOrderDoesNotAffectPeerAndLeecherCalculations()
    {
        var peersFirstXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Peers.First</title>
      <torznab:attr name=""peers"" value=""100""/>
      <torznab:attr name=""seeders"" value=""70""/>
    </item>
  </channel>
</rss>";

        var seedersFirstXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Seeders.First</title>
      <torznab:attr name=""seeders"" value=""70""/>
      <torznab:attr name=""peers"" value=""100""/>
    </item>
  </channel>
</rss>";

        var resultsPeersFirst = this.client.ParseTorznabFeedXml(peersFirstXml);
        var resultsSeedersFirst = this.client.ParseTorznabFeedXml(seedersFirstXml);

        resultsPeersFirst.Should().HaveCount(1);
        resultsSeedersFirst.Should().HaveCount(1);

        resultsPeersFirst[0].Seeders.Should().Be(70);
        resultsPeersFirst[0].Leechers.Should().Be(30);
        resultsPeersFirst[0].Peers.Should().Be(100);

        resultsSeedersFirst[0].Seeders.Should().Be(70);
        resultsSeedersFirst[0].Leechers.Should().Be(30);
        resultsSeedersFirst[0].Peers.Should().Be(100);
    }

    [Test]
    public void ParseTorznabFeedXml_ExtractsMinimumRatioAndMinimumSeedTime()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Ratio.And.SeedTime.Release</title>
      <torznab:attr name=""seeders"" value=""20""/>
      <torznab:attr name=""minimumratio"" value=""1.5""/>
      <torznab:attr name=""minimumseedtime"" value=""172800""/>
    </item>
    <item>
      <title>Default.Release</title>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());

        results.Should().HaveCount(2);
        results[0].MinimumRatio.Should().Be(1.5);
        results[0].MinimumSeedTime.Should().Be(172800L);

        results[1].MinimumRatio.Should().BeNull();
        results[1].MinimumSeedTime.Should().BeNull();
    }

    [Test]
    public void ParseTorznabFeedXml_CategoryWithIdAttribute_ExtractsBothIdAndName()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>TV.HD.Release</title>
      <category id=""5040"">TV/HD</category>
      <category domain=""2000"">Movies/HD</category>
      <category id=""3000"" name=""Audio/Lossless"" />
      <torznab:attr name=""seeders"" value=""15""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml, new IndexerDefinition());

        results.Should().HaveCount(1);
        results[0].Category.Should().Contain("5040");
        results[0].Category.Should().Contain("TV/HD");
        results[0].Category.Should().Contain("2000");
        results[0].Category.Should().Contain("Movies/HD");
        results[0].Category.Should().Contain("3000");
        results[0].Category.Should().Contain("Audio/Lossless");
    }

    #endregion

    #region Torznab Capabilities Parsing (t=caps)

    [Test]
    public void ParseCapabilitiesXml_ValidXml_ExtractsCategoriesSearchModesAndLimits()
    {
        var capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
  <server version=""1.0"" title=""TestTracker"" />
  <limits default=""50"" max=""100"" />
  <searching>
    <search available=""yes"" supportedParams=""q"" />
    <tv-search available=""yes"" supportedParams=""q,season,ep,imdbid,tvdbid"" />
    <movie-search available=""yes"" supportedParams=""q,imdbid,tmdbid"" />
    <music-search available=""no"" supportedParams=""q"" />
  </searching>
  <categories>
    <category id=""2000"" name=""Movies"">
      <subcat id=""2010"" name=""Movies/Foreign"" />
      <subcat id=""2040"" name=""Movies/HD"" />
      <subcat id=""2045"" name=""Movies/UHD"" />
    </category>
    <category id=""5000"" name=""TV"">
      <subcat id=""5030"" name=""TV/SD"" />
      <subcat id=""5040"" name=""TV/HD"" />
    </category>
    <category id=""3000"" name=""Audio"" />
  </categories>
</caps>";

        var caps = this.client.ParseCapabilitiesXml(capsXml);

        caps.Should().NotBeNull();
        caps.DefaultPageSize.Should().Be(50);
        caps.MaxPageSize.Should().Be(100);

        caps.SupportsSearch.Should().BeTrue();
        caps.SupportsTvSearch.Should().BeTrue();
        caps.SupportedTvParams.Should().Contain(new[] { "q", "season", "ep", "imdbid", "tvdbid" });
        caps.SupportsMovieSearch.Should().BeTrue();
        caps.SupportedMovieParams.Should().Contain(new[] { "q", "imdbid", "tmdbid" });
        caps.SupportsMusicSearch.Should().BeFalse();

        caps.Categories.Should().HaveCount(3);
        caps.Categories[0].Id.Should().Be(2000);
        caps.Categories[0].Name.Should().Be("Movies");
        caps.Categories[0].SubCategories.Should().HaveCount(3);
        caps.Categories[0].SubCategories[0].Id.Should().Be(2010);
        caps.Categories[0].SubCategories[0].Name.Should().Be("Movies/Foreign");

        caps.Categories[1].Id.Should().Be(5000);
        caps.Categories[1].Name.Should().Be("TV");
        caps.Categories[1].SubCategories.Should().HaveCount(2);

        caps.Categories[2].Id.Should().Be(3000);
        caps.Categories[2].Name.Should().Be("Audio");
        caps.Categories[2].SubCategories.Should().BeEmpty();
    }

    [Test]
    public void ParseCapabilitiesXml_WhenXmlEmptyOrMalformed_ReturnsDefaultCapabilitiesGracefully()
    {
        var emptyCaps = this.client.ParseCapabilitiesXml(string.Empty);
        emptyCaps.Should().NotBeNull();
        emptyCaps.Categories.Should().BeEmpty();

        var malformedCaps = this.client.ParseCapabilitiesXml("<caps><unclosed>");
        malformedCaps.Should().NotBeNull();
        malformedCaps.Categories.Should().BeEmpty();
    }

    [Test]
    public void ParseTorznabFeedXml_ParsesRfc822AndRfc2822DatesWithOffsetsCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <title>Torznab Feed</title>
    <item>
      <title>Test.Release.2026.1080p</title>
      <guid>https://indexer.local/details/999</guid>
      <link>https://indexer.local/download/999.torrent</link>
      <pubDate>Sun, 06 Sep 2026 18:30:00 +0200</pubDate>
      <torznab:attr name=""seeders"" value=""5""/>
    </item>
  </channel>
</rss>";

        var indexer = new IndexerDefinition { Id = 1, Name = "TrackerTest", MinSeeders = 1 };
        var results = this.client.ParseTorznabFeedXml(xml, indexer);

        results.Should().HaveCount(1);
        results[0].PublishDate.Should().Be(new DateTime(2026, 9, 6, 16, 30, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task SearchAsync_WithExistingQueryParamsInUrl_MergesParametersWithoutDuplication()
    {
        Uri capturedUri = null!;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss><channel><title>Test</title></channel></rss>"),
            };
        });

        var testHttpClient = new HttpClient(handler);
        var customClient = new TorznabClient(testHttpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "CustomTracker",
            Url = "https://indexer.local/api?t=caps&apikey=oldKey&custom=param",
            ApiKey = "newKey",
        };

        await customClient.SearchAsync(indexer, "test movie");

        capturedUri.Should().NotBeNull();
        capturedUri.Query.Should().Contain("apikey=newKey");
        capturedUri.Query.Should().NotContain("apikey=oldKey");
        capturedUri.Query.Should().Contain("custom=param");
        capturedUri.Query.Should().Contain("t=search");
    }

    [Test]
    public async Task TestConnectionAsync_WhenCapsReturnsErrorWithUnescapedAmpersand_ReturnsDescriptiveFailure()
    {
        var handler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<error code=""100"" description=""Invalid credentials & API key"" />"),
            };
        });

        var testHttpClient = new HttpClient(handler);
        var customClient = new TorznabClient(testHttpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "TrackerError",
            Url = "https://indexer.local/api",
            ApiKey = "bad_key",
        };

        var result = await customClient.TestConnectionAsync(indexer);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Torznab error (100): Invalid credentials & API key");
    }

    [Test]
    public async Task TestConnectionAsync_WhenCapsReturnsErrorWithInvalidControlChars_ReturnsDescriptiveFailure()
    {
        var badChars = "\x01\x02\x08\x0B\x0C\x1F";
        var handler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($@"<error code=""200"" description=""Access Denied{badChars}"" />"),
            };
        });

        var testHttpClient = new HttpClient(handler);
        var customClient = new TorznabClient(testHttpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "TrackerControlChars",
            Url = "https://indexer.local/api",
            ApiKey = "key",
        };

        var result = await customClient.TestConnectionAsync(indexer);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Torznab error (200): Access Denied");
    }

    [Test]
    public async Task TestConnectionAsync_WhenSearchFallbackReturnsErrorWithUnescapedAmpersand_ReturnsDescriptiveFailure()
    {
        var handler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Query.Contains("t=caps"))
            {
                // Return something that is not caps and not error, so fallback to search is triggered
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<unknown></unknown>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<error code=""101"" description=""Search query failed & rejected"" />"),
            };
        });

        var testHttpClient = new HttpClient(handler);
        var customClient = new TorznabClient(testHttpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "TrackerFallbackError",
            Url = "https://indexer.local/api",
            ApiKey = "key",
        };

        var result = await customClient.TestConnectionAsync(indexer);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Torznab error (101): Search query failed & rejected");
    }

    [Test]
    public void SafeParseXml_WithUnescapedAmpersandsAndControlCharacters_ParsesDocumentCorrectly()
    {
        var rawXml = "<root attr=\"value & more &amp; &lt;tag&gt; \x01\x02\">Text & Content &#169;</root>";
        var doc = TorznabClient.SafeParseXml(rawXml);

        doc.Should().NotBeNull();
        doc.Root.Should().NotBeNull();
        doc.Root!.Attribute("attr")?.Value.Should().Be("value & more & <tag> ");
        doc.Root!.Value.Should().Be("Text & Content ©");
    }

    [Test]
    public void ParseTorznabFeedXml_WhenCloudflareChallengeHtml_ReturnsEmptyList()
    {
        var challengeHtml = "<html><head><title>Just a moment...</title></head><body>Turnstile challenge</body></html>";
        var indexer = new IndexerDefinition { Name = "ProtectedTracker" };
        var results = this.client.ParseTorznabFeedXml(challengeHtml, indexer);

        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Test]
    public async Task TestConnectionAsync_WhenCloudflareChallengeEncountered_ReturnsDescriptiveErrorMessage()
    {
        var handler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<html><title>Just a moment...</title><body>Checking your browser</body></html>"),
            };
        });

        var testHttpClient = new HttpClient(handler);
        var customClient = new TorznabClient(testHttpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "CfTracker",
            Url = "https://cf.tracker.local/api",
            ApiKey = "key",
        };

        var result = await customClient.TestConnectionAsync(indexer);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cloudflare / AntiBot challenge detected");
    }

    [TestCase("Mon, 01 Jan 2024 12:00:00 EST", "2024-01-01T17:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 EDT", "2024-01-01T16:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 PST", "2024-01-01T20:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 PDT", "2024-01-01T19:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 CEST", "2024-01-01T10:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 BST", "2024-01-01T11:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 JST", "2024-01-01T03:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 UTC", "2024-01-01T12:00:00Z")]
    [TestCase("Mon, 01 Jan 2024 12:00:00 GMT", "2024-01-01T12:00:00Z")]
    [TestCase("1704110400", "2024-01-01T12:00:00Z")]
    [TestCase("1704110400000", "2024-01-01T12:00:00Z")]
    public void ParseTorznabFeedXml_TimezonesAndUnixTimestamps_ParsesCorrectly(string pubDateInput, string expectedUtcIso)
    {
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Timezone Test Release</title>
      <pubDate>{pubDateInput}</pubDate>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);
        results.Should().HaveCount(1);
        results[0].PublishDate.Should().Be(DateTime.Parse(expectedUtcIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    [Test]
    public void ParseTorznabFeedXml_Base32InfoHash_NormalizesToLowercaseHex()
    {
        // Base32 "JBSWY3DPEBLW64TMMQQQ====" -> hex "48656c6c6f21deadbeef" (40 chars)
        // 32 chars: "4R7W26T6Z2B4X5J4L7OQ6Z2B4X5J4L7O" -> 40 chars hex
        var base32Hash = "4R7W26T6Z2B4X5J4L7OQ6Z2B4X5J4L7O";
        var expectedHex = NzbDrone.Core.Torrents.MagnetLinkParser.Base32ToHex(base32Hash).ToLowerInvariant();

        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Base32 Infohash Release</title>
      <torznab:attr name=""infohash"" value=""{base32Hash}""/>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);
        results.Should().HaveCount(1);
        results[0].InfoHash.Should().Be(expectedHex);
        results[0].InfoHash.Length.Should().Be(40);
    }

    [Test]
    public void ParseTorznabFeedXml_WhenInfoHashOmitted_AutoPopulatesFromMagnetUrl()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Omitted Infohash Release</title>
      <torznab:attr name=""magneturl"" value=""magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01&amp;dn=Release""/>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);
        results.Should().HaveCount(1);
        results[0].InfoHash.Should().Be("abcdef0123456789abcdef0123456789abcdef01");
    }

    [Test]
    public void ParseTorznabFeedXml_WhenNoEnclosureOrMagnetAttrAndLinkIsHtml_ExtractsMagnetFromDescriptionAndContentEncoded()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:content=""http://purl.org/rss/1.0/modules/content/"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Description HTML Magnet Release</title>
      <link>https://indexer.local/viewtopic.php?t=12345</link>
      <description>&lt;p&gt;Download: &lt;a href=""magnet:?xt=urn:btih:1111111111222222222233333333334444444444&amp;amp;dn=Test1""&gt;Magnet&lt;/a&gt;&lt;/p&gt;</description>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
    <item>
      <title>Content Encoded HTML Magnet Release</title>
      <link>https://indexer.local/details.php?id=999</link>
      <content:encoded><![CDATA[<div>Direct magnet: magnet:?xt=urn:btih:5555555555666666666677777777778888888888&dn=Test2</div>]]></content:encoded>
      <torznab:attr name=""seeders"" value=""5""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);
        results.Should().HaveCount(2);

        results[0].MagnetUrl.Should().Be("magnet:?xt=urn:btih:1111111111222222222233333333334444444444&dn=Test1");
        results[0].InfoHash.Should().Be("1111111111222222222233333333334444444444");

        results[1].MagnetUrl.Should().Be("magnet:?xt=urn:btih:5555555555666666666677777777778888888888&dn=Test2");
        results[1].InfoHash.Should().Be("5555555555666666666677777777778888888888");
    }

    #region Capabilities TTL Cache & Namespace Resilience & Newznab Mappings

    [Test]
    public async Task FetchCapabilitiesAsync_WithTtlCache_CachesResultAndAvoidsDuplicateHttpRequests()
    {
        TorznabClient.ClearCapabilitiesCache();

        var requestCount = 0;
        var capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
  <server version=""1.0"" title=""CachedTracker"" />
  <limits default=""25"" max=""75"" />
  <searching>
    <search available=""yes"" />
    <tv-search available=""yes"" supportedParams=""q,season,ep"" />
  </searching>
  <categories>
    <category id=""5000"" name=""TV"" />
  </categories>
</caps>";

        var handler = new TestHttpMessageHandler(req =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(capsXml),
            };
        });

        var customClient = new TorznabClient(new HttpClient(handler));
        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "CacheTestTracker",
            Url = "https://cache.indexer.local/api",
            ApiKey = "secret123",
        };

        var caps1 = await customClient.FetchCapabilitiesAsync(indexer);
        caps1.Should().NotBeNull();
        caps1.DefaultPageSize.Should().Be(25);
        caps1.MaxPageSize.Should().Be(75);
        caps1.SupportsTvSearch.Should().BeTrue();
        requestCount.Should().Be(1);

        // Second call should hit the in-memory cache and not make an HTTP request
        var caps2 = await customClient.FetchCapabilitiesAsync(indexer);
        caps2.Should().NotBeNull();
        caps2.DefaultPageSize.Should().Be(25);
        requestCount.Should().Be(1);

        // Invalidate and verify a new request is made
        TorznabClient.InvalidateCapabilities(indexer.Url, indexer.ApiKey);
        var caps3 = await customClient.FetchCapabilitiesAsync(indexer);
        caps3.Should().NotBeNull();
        requestCount.Should().Be(2);
    }

    [Test]
    public void ParseCapabilitiesXml_WithArbitraryXmlNamespacesAndPascalCaseTags_ParsesCorrectly()
    {
        var xmlWithNamespaces = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Caps xmlns=""http://torznab.com/schemas/2015/feed"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"" xmlns:custom=""http://custom.namespace/org"">
  <Server version=""2.0"" title=""NamespacedTracker"" />
  <Limits Default=""30"" Max=""150"" />
  <Searching>
    <Search Available=""yes"" SupportedParams=""q"" />
    <Tv-Search Available=""yes"" SupportedParams=""q,season,ep,tvdbid,rid"" />
    <Movie-Search Available=""yes"" SupportedParams=""q,imdbid,tmdbid,year"" />
    <Music-Search Available=""yes"" SupportedParams=""q,artist,album"" />
    <Book-Search Available=""yes"" SupportedParams=""q,author,isbn"" />
  </Searching>
  <Categories>
    <Category Id=""2000"" Name=""Movies"">
      <SubCat Id=""2040"" Name=""Movies/HD"" />
      <SubCategory Id=""2050"" Name=""Movies/3D"" />
    </Category>
    <Category Id=""5000"" Name=""TV"">
      <SubCat Id=""5040"" Name=""TV/HD"" />
    </Category>
    <Category Id=""7000"" Name=""Books"">
      <SubCat Id=""7020"" Name=""EBook"" />
    </Category>
  </Categories>
</Caps>";

        var caps = this.client.ParseCapabilitiesXml(xmlWithNamespaces);

        caps.Should().NotBeNull();
        caps.DefaultPageSize.Should().Be(30);
        caps.MaxPageSize.Should().Be(150);
        caps.SupportsSearch.Should().BeTrue();
        caps.SupportsTvSearch.Should().BeTrue();
        caps.SupportedTvParams.Should().Contain(new[] { "q", "season", "ep", "tvdbid", "rid" });
        caps.SupportsMovieSearch.Should().BeTrue();
        caps.SupportedMovieParams.Should().Contain(new[] { "q", "imdbid", "tmdbid", "year" });
        caps.SupportsMusicSearch.Should().BeTrue();
        caps.SupportedMusicParams.Should().Contain(new[] { "q", "artist", "album" });
        caps.SupportsBookSearch.Should().BeTrue();
        caps.SupportedBookParams.Should().Contain(new[] { "q", "author", "isbn" });

        caps.Categories.Should().HaveCount(3);
        var movieCat = caps.Categories.FirstOrDefault(c => c.Id == 2000);
        movieCat.Should().NotBeNull();
        movieCat!.SubCategories.Should().HaveCount(2);
        movieCat.SubCategories[0].Id.Should().Be(2040);
        movieCat.SubCategories[1].Id.Should().Be(2050);
    }

    [Test]
    public async Task SearchAsync_WithNewznabUsenetParameters_BuildsCorrectQueryString()
    {
        Uri capturedUri = null!;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss><channel><title>Results</title></channel></rss>"),
            };
        });

        var customClient = new TorznabClient(new HttpClient(handler));
        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "NewznabTracker",
            Url = "https://newznab.local/api",
            ApiKey = "api123",
        };

        // Test with TV parameters (tvdbid, rid, season, ep)
        await customClient.SearchAsync(
            indexer,
            query: "Breaking Bad",
            season: 5,
            ep: 14,
            tvdbId: "81189",
            rid: "1234",
            year: 2013);

        capturedUri.Should().NotBeNull();
        capturedUri.Query.Should().Contain("t=tvsearch");
        capturedUri.Query.Should().Contain("tvdbid=81189");
        capturedUri.Query.Should().Contain("rid=1234");
        capturedUri.Query.Should().Contain("season=5");
        capturedUri.Query.Should().Contain("ep=14");
        capturedUri.Query.Should().Contain("year=2013");
        capturedUri.Query.Should().MatchRegex(@"q=Breaking(\+|%20)Bad");

        // Test with Music parameters (artist, album)
        await customClient.SearchAsync(
            indexer,
            query: "The Dark Side of the Moon",
            artist: "Pink Floyd",
            album: "The Dark Side of the Moon",
            year: 1973);

        capturedUri.Query.Should().Contain("t=music");
        capturedUri.Query.Should().MatchRegex(@"artist=Pink(\+|%20)Floyd");
        capturedUri.Query.Should().MatchRegex(@"album=The(\+|%20)Dark(\+|%20)Side(\+|%20)of(\+|%20)the(\+|%20)Moon");
        capturedUri.Query.Should().Contain("year=1973");

        // Test with Book parameters (author, isbn)
        await customClient.SearchAsync(
            indexer,
            query: "Dune",
            author: "Frank Herbert",
            isbn: "9780441172719");

        capturedUri.Query.Should().Contain("t=book");
        capturedUri.Query.Should().MatchRegex(@"author=Frank(\+|%20)Herbert");
        capturedUri.Query.Should().Contain("isbn=9780441172719");
    }

    [Test]
    public async Task SearchAsync_WithImdbId_NormalizesAndStripsTtPrefix()
    {
        Uri capturedUri = null!;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss><channel><title>Results</title></channel></rss>"),
            };
        });

        var customClient = new TorznabClient(new HttpClient(handler));
        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "MovieTracker",
            Url = "https://movies.local/api",
            ApiKey = "key",
        };

        // IMDb with tt prefix
        await customClient.SearchAsync(indexer, query: "Inception", imdbId: "tt1375666");
        capturedUri.Query.Should().Contain("t=movie");
        capturedUri.Query.Should().Contain("imdbid=1375666");
        capturedUri.Query.Should().NotContain("imdbid=tt1375666");

        // IMDb without tt prefix (already numeric)
        await customClient.SearchAsync(indexer, query: "Inception", imdbId: "1375666");
        capturedUri.Query.Should().Contain("imdbid=1375666");
    }

    [Test]
    public void ParseTorznabFeedXml_CaseInsensitiveAttributes_ParsesCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Case Insensitive Attr Test</title>
      <guid>12345</guid>
      <enclosure URL=""https://tracker.local/dl.torrent"" LENGTH=""5000000000"" TYPE=""application/x-bittorrent"" />
      <category ID=""2000"" NAME=""Movies/HD"" />
      <torznab:attr NAME=""Seeders"" VALUE=""88""/>
      <torznab:attr NAME=""LEECHERS"" VALUE=""12""/>
      <torznab:attr Name=""DOWNLOADVOLUMEFACTOR"" Value=""0.5""/>
      <torznab:attr Name=""UploadVolumeFactor"" Value=""2.0""/>
      <torznab:attr Name=""INFOHASH"" Value=""0123456789ABCDEF0123456789ABCDEF01234567""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        var r = results[0];
        r.DownloadUrl.Should().Be("https://tracker.local/dl.torrent");
        r.Size.Should().Be(5000000000L);
        r.Seeders.Should().Be(88);
        r.Leechers.Should().Be(12);
        r.DownloadVolumeFactor.Should().Be(0.5);
        r.UploadVolumeFactor.Should().Be(2.0);
        r.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        r.Category.Should().Contain("2000");
        r.Category.Should().Contain("Movies/HD");
    }

    [Test]
    public void ParseTorznabFeedXml_ExplicitDownloadVolumeFactor_TakesPrecedenceOverFreeleechFlag()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Precedence Test 1</title>
      <freeleech>1</freeleech>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""freeleech"" value=""1""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0.5""/>
    </item>
    <item>
      <title>Precedence Test 2</title>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0.75""/>
      <torznab:attr name=""freeleech"" value=""1""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(2);
        results[0].DownloadVolumeFactor.Should().Be(0.5);
        results[0].IsFreeleech.Should().BeFalse();

        results[1].DownloadVolumeFactor.Should().Be(0.75);
        results[1].IsFreeleech.Should().BeFalse();
    }

    [Test]
    public void ParseTorznabFeedXml_CommaDecimalSeparators_ParsesDoubleCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title>Comma Decimal Separator Test</title>
      <torznab:attr name=""seeders"" value=""20""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0,5""/>
      <torznab:attr name=""uploadvolumefactor"" value=""1,5""/>
      <torznab:attr name=""minimumratio"" value=""1,25""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].DownloadVolumeFactor.Should().Be(0.5);
        results[0].UploadVolumeFactor.Should().Be(1.5);
        results[0].MinimumRatio.Should().Be(1.25);
    }

    [Test]
    public void ResolveEffectiveLimit_WithConfiguredLimits_AppliesDefaultsAndCaps()
    {
        var configService = NSubstitute.Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.TorznabDefaultPageSize.Returns(75);
        configService.TorznabMaxPageSize.Returns(150);

        var clientWithConfig = new TorznabClient(configService);
        var indexer = new IndexerDefinition { Id = 1, Name = "TestIndexer", Url = "http://localhost" };

        // When 0 requested, uses configured default (75)
        clientWithConfig.ResolveEffectiveLimit(indexer, 0).Should().Be(75);

        // When requested limit is 200, caps at configured max (150)
        clientWithConfig.ResolveEffectiveLimit(indexer, 200).Should().Be(150);

        // When requested limit is within range (100), preserves requested limit
        clientWithConfig.ResolveEffectiveLimit(indexer, 100).Should().Be(100);
    }

    [Test]
    public void ParseTorznabFeedXml_WhenTitleContainsHtmlEntitiesInCData_DecodesHtmlEntitiesCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title><![CDATA[Bob&#039;s.Burgers.S12E01.1080p.WEB-DL.DDP5.1.H.264-FLUX]]></title>
      <guid>https://indexer.local/details/101</guid>
      <torznab:attr name=""seeders"" value=""10""/>
    </item>
    <item>
      <title><![CDATA[Tom &amp; Jerry 2021 1080p Bluray x264-SPARKS]]></title>
      <guid>https://indexer.local/details/102</guid>
      <torznab:attr name=""seeders"" value=""15""/>
    </item>
    <item>
      <title><![CDATA[&quot;The.Great&#39;s.Show&quot; &lt;Special Edition&gt;]]></title>
      <guid>https://indexer.local/details/103</guid>
      <torznab:attr name=""seeders"" value=""5""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(3);
        results[0].Title.Should().Be("Bob's.Burgers.S12E01.1080p.WEB-DL.DDP5.1.H.264-FLUX");
        results[1].Title.Should().Be("Tom & Jerry 2021 1080p Bluray x264-SPARKS");
        results[2].Title.Should().Be("\"The.Great's.Show\" <Special Edition>");
    }

    [Test]
    public void ParseTorznabFeedXml_WhenDescriptionAndCategoriesContainHtmlEntities_DecodesCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <item>
      <title><![CDATA[Marvel&#39;s Agents of S.H.I.E.L.D. S01E01]]></title>
      <description><![CDATA[High-Definition 1080p &amp; 5.1 Audio &lt;HDTV&gt;]]></description>
      <comments><![CDATA[https://indexer.local/details?id=123&amp;page=1]]></comments>
      <category><![CDATA[TV &gt; HD &amp; UHD]]></category>
      <torznab:attr name=""seeders"" value=""10""/>
      <torznab:attr name=""category"" value=""TV &amp; Series""/>
    </item>
  </channel>
</rss>";

        var results = this.client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Marvel's Agents of S.H.I.E.L.D. S01E01");
        results[0].Description.Should().Be("High-Definition 1080p & 5.1 Audio <HDTV>");
        results[0].Comments.Should().Be("https://indexer.local/details?id=123&page=1");
        results[0].Category.Should().Contain("TV > HD & UHD");
        results[0].Category.Should().Contain("TV & Series");
    }

    #endregion

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }

    #endregion
}
