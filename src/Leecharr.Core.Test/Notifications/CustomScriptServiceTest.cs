// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class CustomScriptServiceTest
{
    private CustomScriptService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new CustomScriptService();
    }

    [Test]
    public async Task ExecuteScriptAsync_WhenScriptDoesNotExist_ReturnsFalse()
    {
        var result = await this.service.ExecuteScriptAsync("/path/to/nonexistent/script.sh", new Torrent(), "OnDownloadComplete");
        result.Should().BeFalse();
    }

    [Test]
    public void BuildEnvironmentVariables_UnderNonEnglishCulture_FormatsDecimalsWithPeriod()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var torrent = new Torrent
            {
                Id = 123,
                Name = "Sample Torrent",
                Ratio = 1.75,
                TotalSize = 1048576,
            };

            var meta = new NzbDrone.Core.MediaEnrichment.TorrentMediaMetadata
            {
                TorrentId = 123,
                Title = "Sample Movie",
                Year = 2024,
                Rating = 8.5,
            };

            var env = CustomScriptService.BuildEnvironmentVariables("OnDownloadComplete", torrent, meta);

            env["TORRENT_RATIO"].Should().Be("1.75");
            env["LEECHARR_TORRENT_RATIO"].Should().Be("1.75");
            env["LEECHARR_MEDIA_RATING"].Should().Be("8.5");
            env["TORRENT_ID"].Should().Be("123");
            env["LEECHARR_MEDIA_YEAR"].Should().Be("2024");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
