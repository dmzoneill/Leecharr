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
}
