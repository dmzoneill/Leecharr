// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Network.GeoIp;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class IP2LocationGeoIpProviderTest
{
    private string tempDbFile = null!;
    private IDiskProvider diskProvider = null!;
    private IAppFolderInfo appFolderInfo = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDbFile = Path.Combine(Path.GetTempPath(), $"ip2location_test_{Guid.NewGuid():N}.bin");
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());
        this.appFolderInfo.StartUpFolder.Returns(Path.GetTempPath());
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(this.tempDbFile))
        {
            try
            {
                File.Delete(this.tempDbFile);
            }
            catch
            {
                // Ignored in teardown
            }
        }
    }

    [Test]
    public async Task LookupAsync_WhenDatabaseMissing_ReturnsUnenrichedInfo()
    {
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        using var provider = new IP2LocationGeoIpProvider(this.diskProvider, this.appFolderInfo);
        provider.IsAvailable.Should().BeFalse();

        var result = await provider.LookupAsync("8.8.8.8");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("8.8.8.8");
        result.CountryCode.Should().BeNull();
    }

    [Test]
    public async Task LookupAsync_WhenIpAddressInvalid_ReturnsUnenrichedInfo()
    {
        CreateSampleBinaryDatabase(this.tempDbFile);
        this.diskProvider.FileExists(this.tempDbFile).Returns(true);
        this.appFolderInfo.AppDataFolder.Returns(Path.GetDirectoryName(this.tempDbFile)!);

        using var provider = new IP2LocationGeoIpProvider(this.diskProvider, this.appFolderInfo);

        var nullResult = await provider.LookupAsync(null!);
        nullResult.Should().BeNull();

        var emptyResult = await provider.LookupAsync("   ");
        emptyResult.Should().BeNull();

        var invalidResult = await provider.LookupAsync("not-an-ip");
        invalidResult.Should().NotBeNull();
        invalidResult.IpAddress.Should().Be("not-an-ip");
        invalidResult.CountryCode.Should().BeNull();
    }

    [Test]
    public async Task LookupAsync_WithIPv4Address_ResolvesCountry()
    {
        CreateSampleBinaryDatabase(this.tempDbFile);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(callInfo => (string)callInfo[0] == this.tempDbFile);
        this.appFolderInfo.AppDataFolder.Returns(Path.GetDirectoryName(this.tempDbFile)!);

        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(Arg.Is<string>(s => s.Contains("IP2Location.BIN"))).Returns(false);
        diskMock.FileExists(this.tempDbFile).Returns(true);

        using var provider = new TestableIP2LocationGeoIpProvider(this.tempDbFile, diskMock, this.appFolderInfo);

        var result = await provider.LookupAsync("8.8.8.8");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("8.8.8.8");
        result.CountryCode.Should().Be("US");
        result.CountryName.Should().Be("United States");
    }

    [Test]
    public async Task LookupAsync_WithIPv4MappedIPv6Address_UnmapsAndResolvesCountry()
    {
        CreateSampleBinaryDatabase(this.tempDbFile);
        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(this.tempDbFile).Returns(true);

        using var provider = new TestableIP2LocationGeoIpProvider(this.tempDbFile, diskMock, this.appFolderInfo);

        var result = await provider.LookupAsync("::ffff:8.8.8.8");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("::ffff:8.8.8.8");
        result.CountryCode.Should().Be("US");
        result.CountryName.Should().Be("United States");
    }

    [Test]
    public async Task LookupAsync_WithNativeIPv6Address_ResolvesCountryViaIPv6BinarySearch()
    {
        CreateSampleBinaryDatabase(this.tempDbFile);
        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(this.tempDbFile).Returns(true);

        using var provider = new TestableIP2LocationGeoIpProvider(this.tempDbFile, diskMock, this.appFolderInfo);

        var result = await provider.LookupAsync("2001:db8:1::42");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("2001:db8:1::42");
        result.CountryCode.Should().Be("GB");
        result.CountryName.Should().Be("United Kingdom");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenDatabaseValid_ReturnsHealthy()
    {
        CreateSampleBinaryDatabase(this.tempDbFile);
        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.FileExists(this.tempDbFile).Returns(true);

        using var provider = new TestableIP2LocationGeoIpProvider(this.tempDbFile, diskMock, this.appFolderInfo);

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("IP2Location database loaded successfully");
    }

    private static void CreateSampleBinaryDatabase(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // Header: 21 bytes (offsets 0..20)
        // IPv4 Records: 3 entries * 8 bytes = 24 bytes (offsets 21..44). Base address (1-based) = 22
        // IPv6 Records: 3 entries * 20 bytes = 60 bytes (offsets 45..104). Base address (1-based) = 46
        // Country String 1 (US): offset 105
        // Country String 2 (GB): offset 122
        const uint baseAddrIPv4 = 22;
        const uint baseAddrIPv6 = 46;
        const uint countryOffsetUS = 105;
        const uint countryOffsetGB = 122;

        // Header
        writer.Write((byte)1); // dbType
        writer.Write((byte)2); // dbColumn (IP From + Country)
        writer.Write((byte)26); // year
        writer.Write((byte)9); // month
        writer.Write((byte)5); // day
        writer.Write(2u); // ipv4Count
        writer.Write(baseAddrIPv4); // baseAddress (1-based)
        writer.Write(2u); // ipv6Count
        writer.Write(baseAddrIPv6); // baseAddressIPv6 (1-based)

        // IPv4 Records
        // Record 0: 0.0.0.0 - 9.0.0.0 -> US
        var ipBytes1 = IPAddress.Parse("0.0.0.0").GetAddressBytes();
        Array.Reverse(ipBytes1);
        writer.Write(BitConverter.ToUInt32(ipBytes1, 0));
        writer.Write(countryOffsetUS);

        // Record 1: 9.0.0.0 - 255.255.255.255 -> GB
        var ipBytes2 = IPAddress.Parse("9.0.0.0").GetAddressBytes();
        Array.Reverse(ipBytes2);
        writer.Write(BitConverter.ToUInt32(ipBytes2, 0));
        writer.Write(countryOffsetGB);

        // Record 2 (terminator): 255.255.255.255
        var ipBytesTerm = IPAddress.Parse("255.255.255.255").GetAddressBytes();
        Array.Reverse(ipBytesTerm);
        writer.Write(BitConverter.ToUInt32(ipBytesTerm, 0));
        writer.Write(0u);

        // IPv6 Records
        // Record 0: :: to 2001:db8:1:: -> US
        var ipv6Bytes0 = new byte[16];
        writer.Write(ipv6Bytes0);
        writer.Write(countryOffsetUS);

        // Record 1: 2001:db8:1:: to ffff:ffff:... -> GB
        var ipv6Addr1 = IPAddress.Parse("2001:db8:1::").GetAddressBytes();
        var ip1BigInt = new BigInteger(ipv6Addr1, isUnsigned: true, isBigEndian: true);
        var ip1LeBytes = new byte[16];
        var exported = ip1BigInt.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Copy(exported, ip1LeBytes, Math.Min(exported.Length, 16));
        writer.Write(ip1LeBytes);
        writer.Write(countryOffsetGB);

        // Record 2 (terminator): ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff
        var ipv6Term = new byte[16];
        Array.Fill(ipv6Term, (byte)0xFF);
        writer.Write(ipv6Term);
        writer.Write(0u);

        // Country Strings
        // US (offset 105)
        var usCode = Encoding.ASCII.GetBytes("US");
        var usName = Encoding.ASCII.GetBytes("United States");
        writer.Write((byte)usCode.Length);
        writer.Write(usCode);
        writer.Write((byte)usName.Length);
        writer.Write(usName);

        // GB (offset 122)
        var gbCode = Encoding.ASCII.GetBytes("GB");
        var gbName = Encoding.ASCII.GetBytes("United Kingdom");
        writer.Write((byte)gbCode.Length);
        writer.Write(gbCode);
        writer.Write((byte)gbName.Length);
        writer.Write(gbName);
    }

    private class TestableIP2LocationGeoIpProvider : IP2LocationGeoIpProvider
    {
        private readonly string dbPath;

        public TestableIP2LocationGeoIpProvider(string dbPath, IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
            : base(diskProvider, appFolderInfo)
        {
            this.dbPath = dbPath;
        }

        public override string GetDatabasePath() => this.dbPath;
    }
}
