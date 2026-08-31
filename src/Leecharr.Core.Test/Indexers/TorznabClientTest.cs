using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class TorznabClientTest
{
    private TorznabClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new TorznabClient();
    }

    [Test]
    public void ParseTorznabFeedXml_ExtractsReleasesAndFreeleechAttribute()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
  <channel>
    <title>Torznab Indexer Feed</title>
    <item>
      <title>Dune.Part.Two.2024.2160p.UHD.HDR.TrueHD.Atmos.7.1-FLUX</title>
      <guid>https://indexer.local/details/12345</guid>
      <link>https://indexer.local/download/12345.torrent</link>
      <size>45000000000</size>
      <torznab:attr name=""seeders"" value=""150""/>
      <torznab:attr name=""peers"" value=""25""/>
      <torznab:attr name=""downloadvolumefactor"" value=""0""/>
      <torznab:attr name=""infohash"" value=""0123456789ABCDEF0123456789ABCDEF01234567""/>
      <torznab:attr name=""magneturl"" value=""magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567""/>
    </item>
    <item>
      <title>Severance.S02E01.1080p.WEB-DL.x265</title>
      <guid>https://indexer.local/details/12346</guid>
      <link>https://indexer.local/download/12346.torrent</link>
      <size>2500000000</size>
      <torznab:attr name=""seeders"" value=""50""/>
      <torznab:attr name=""peers"" value=""5""/>
      <torznab:attr name=""downloadvolumefactor"" value=""1""/>
    </item>
  </channel>
</rss>";

        var indexer = new IndexerDefinition { Name = "TrackerAlpha", FreeleechOnly = false, MinSeeders = 1 };
        var results = _client.ParseTorznabFeedXml(xml, indexer);

        results.Should().HaveCount(2);

        var first = results[0];
        first.Title.Should().Be("Dune.Part.Two.2024.2160p.UHD.HDR.TrueHD.Atmos.7.1-FLUX");
        first.Seeders.Should().Be(150);
        first.Leechers.Should().Be(25);
        first.DownloadVolumeFactor.Should().Be(0.0);
        first.IsFreeleech.Should().BeTrue();
        first.InfoHash.Should().Be("0123456789ABCDEF0123456789ABCDEF01234567");
        first.MagnetUrl.Should().StartWith("magnet:?");

        var second = results[1];
        second.IsFreeleech.Should().BeFalse();
        second.Seeders.Should().Be(50);
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
  </channel>
</rss>";

        var indexer = new IndexerDefinition { Name = "TrackerBeta", FreeleechOnly = true, MinSeeders = 1 };
        var results = _client.ParseTorznabFeedXml(xml, indexer);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Release 1 (Freeleech)");
    }
}
