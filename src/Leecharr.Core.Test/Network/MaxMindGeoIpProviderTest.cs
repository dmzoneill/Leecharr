// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Network.GeoIp;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class MaxMindGeoIpProviderTest
{
    private IDiskProvider diskProvider = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private string testDatabasePath = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());
        this.appFolderInfo.StartUpFolder.Returns(Path.GetTempPath());

        this.testDatabasePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "TestData",
            "GeoIP2-Country-Test.mmdb");
    }

    [Test]
    public async Task LookupAsync_WithTestDatabase_ResolvesCountry()
    {
        File.Exists(this.testDatabasePath).Should().BeTrue("MaxMind test database must be copied to output");

        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(this.testDatabasePath).Returns(true);

        using var provider = new TestableMaxMindGeoIpProvider(this.testDatabasePath, diskMock, this.appFolderInfo);

        var result = await provider.LookupAsync("2.125.160.216");

        result.Should().NotBeNull();
        result.IpAddress.Should().Be("2.125.160.216");
        result.CountryCode.Should().Be("GB");
        result.CountryName.Should().Be("United Kingdom");
    }

    [Test]
    public async Task Concurrent_LookupAsync_And_Reload_ExecutesSafelyWithoutDisposedReader()
    {
        File.Exists(this.testDatabasePath).Should().BeTrue("MaxMind test database must be copied to output");

        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(this.testDatabasePath).Returns(true);

        using var provider = new TestableMaxMindGeoIpProvider(this.testDatabasePath, diskMock, this.appFolderInfo);

        var warmup = await provider.LookupAsync("2.125.160.216");
        warmup.CountryCode.Should().Be("GB");

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => provider.Reload()));
            tasks.Add(Task.Run(async () =>
            {
                var lookup = await provider.LookupAsync("2.125.160.216");
                lookup.Should().NotBeNull();
                lookup.CountryCode.Should().Be("GB");
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Test]
    public async Task Concurrent_ProbeHealthAsync_And_LookupAsync_ExecutesSafelyWithoutStateCorruption()
    {
        File.Exists(this.testDatabasePath).Should().BeTrue("MaxMind test database must be copied to output");

        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(this.testDatabasePath).Returns(true);

        using var provider = new TestableMaxMindGeoIpProvider(this.testDatabasePath, diskMock, this.appFolderInfo);

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var health = await provider.ProbeHealthAsync();
                health.IsHealthy.Should().BeTrue();
            }));

            tasks.Add(Task.Run(async () =>
            {
                var lookup = await provider.LookupAsync("2.125.160.216");
                lookup.Should().NotBeNull();
                lookup.CountryCode.Should().Be("GB");
            }));
        }

        await Task.WhenAll(tasks);
    }

    private class TestableMaxMindGeoIpProvider : MaxMindGeoIpProvider
    {
        private readonly string dbPath;

        public TestableMaxMindGeoIpProvider(string dbPath, IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
            : base(diskProvider, appFolderInfo)
        {
            this.dbPath = dbPath;
        }

        public override string GetDatabasePath() => this.dbPath;
    }
}
