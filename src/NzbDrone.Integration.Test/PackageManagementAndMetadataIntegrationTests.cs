// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Packages;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class PackageManagementAndMetadataIntegrationTests : IntegrationTestBase
{
    [Test]
    public void PackageController_DeterminePackageFileName_GeneratesCorrectNames()
    {
        // 1. Empty list
        PackageController.DeterminePackageFileName(null).Should().Be("package.leecharr");
        PackageController.DeterminePackageFileName(new List<Torrent>()).Should().Be("package.leecharr");

        // 2. Single torrent
        var single = new List<Torrent>
        {
            new Torrent { Name = "Ubuntu.24.04.LTS.Desktop.iso" },
        };
        PackageController.DeterminePackageFileName(single).Should().Be("Ubuntu.24.04.LTS.Desktop.iso.leecharr");

        // 3. Multiple torrents
        var multiple = new List<Torrent>
        {
            new Torrent { Name = "Torrent.One" },
            new Torrent { Name = "Torrent.Two" },
        };
        PackageController.DeterminePackageFileName(multiple).Should().Be("package.leecharr");
    }

    [Test]
    public async Task PackageController_ExportValidation_ReturnsBadRequestWhenNoIds()
    {
        // 1. GET /api/v1/packages/export without torrentIds
        var getResp = await this.Client.GetAsync("/api/v1/packages/export");
        getResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. POST /api/v1/packages/export with empty list
        var postResp = await this.PostJsonAsync("/api/v1/packages/export", new
        {
            torrentIds = new List<int>(),
        });
        postResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
