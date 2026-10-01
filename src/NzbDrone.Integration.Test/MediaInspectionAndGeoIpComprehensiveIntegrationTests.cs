// Copyright (c) FeedItOut. All rights reserved.

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
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.GeoIp;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class MediaInspectionAndGeoIpComprehensiveIntegrationTests : IntegrationTestBase
{
    private string tempDirectory = null!;
    private string tempDbFile = null!;

    [SetUp]
    public void SetUp()
    {
        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        this.tempDirectory = Path.Combine(baseDir, "mediainfo_geoip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);
        this.tempDbFile = Path.Combine(this.tempDirectory, "IP2Location.BIN");
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
            }
        }
    }

    [Test]
    public void MediaInfoInspector_ParseMediaInfoJson_ParsesAdvancedDolbyVisionAndAtmosProfiles()
    {
        // 1. UHD Blu-ray Remux with Dolby Vision, HDR10+, TrueHD Atmos, DTS:X, and subtitles
        var complexJson = """
        {
            "media": {
                "track": [
                    {
                        "@type": "General",
                        "Format": "Matroska",
                        "Duration": "7320.5",
                        "OverallBitRate": "68500000"
                    },
                    {
                        "@type": "Video",
                        "Format": "HEVC",
                        "Format_Profile": "Main 10",
                        "Width": "3840",
                        "Height": "2160",
                        "FrameRate": "23.976",
                        "HDR_Format": "Dolby Vision, Version 1.0, dvhe.07.06, BL+EL+RPU, Blu-ray compatible / SMPTE ST 2086, HDR10 compatible",
                        "HDR_Format_Commercial": "Dolby Vision / HDR10+",
                        "HDR_Format_Compatibility": "HDR10"
                    },
                    {
                        "@type": "Audio",
                        "Format": "MLP FBA 16-ch",
                        "Format_Commercial": "Dolby TrueHD with Dolby Atmos",
                        "Channels": "8",
                        "Language": "en",
                        "Title": "English TrueHD Atmos 7.1"
                    },
                    {
                        "@type": "Audio",
                        "Format": "DTS-HD",
                        "Format_Commercial": "DTS:X",
                        "Channels": "8",
                        "Language": "ja",
                        "Title": "Japanese DTS:X 7.1"
                    },
                    {
                        "@type": "Text",
                        "Format": "PGS",
                        "Language": "en",
                        "Title": "English SDH",
                        "Forced": "No"
                    },
                    {
                        "@type": "Text",
                        "Format": "UTF-8",
                        "Language": "es",
                        "Title": "Spanish Forced",
                        "Forced": "Yes"
                    }
                ]
            }
        }
        """;

        var container = MediaInfoInspectorProvider.ParseMediaInfoJson(complexJson, "Movie.2160p.UHD.Remux.mkv");
        container.Should().NotBeNull();
        container.ContainerFormat.Should().Be("Matroska");
        container.DurationSeconds.Should().BeApproximately(7320.5, 0.1);
        container.VideoCodec.Should().Be("HEVC");
        container.Width.Should().Be(3840);
        container.Height.Should().Be(2160);
        container.HdrFormat.Should().Be("Dolby Vision / HDR10+");
        container.AudioCodec.Should().NotBeNullOrEmpty();
        container.AudioChannels.Should().Be("7.1");
        container.SubtitleTracks.Should().HaveCount(2);
        container.SubtitleTracks[0].Should().Contain("English SDH");
        container.SubtitleTracks[1].Should().Contain("Spanish Forced");

        // 2. Empty / malformed JSON handling
        var emptyJson = "{}";
        var emptyContainer = MediaInfoInspectorProvider.ParseMediaInfoJson(emptyJson, "empty.mp4");
        emptyContainer.Should().BeNull();

        var invalidJson = "this is not json at all";
        var invalidContainer = MediaInfoInspectorProvider.ParseMediaInfoJson(invalidJson, "invalid.mkv");
        invalidContainer.Should().BeNull();
    }

    [Test]
    public async Task IP2LocationGeoIpProvider_WithBinaryDatabase_ResolvesIPv4AndIPv6Cleanly()
    {
        CreateSampleBinaryDatabase(this.tempDbFile);

        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.FileExists(Arg.Any<string>()).Returns(call => File.Exists(call.Arg<string>()));
        diskProvider.FolderExists(Arg.Any<string>()).Returns(call => Directory.Exists(call.Arg<string>()));

        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempDirectory);
        appFolderInfo.StartUpFolder.Returns(this.tempDirectory);

        using var provider = new IP2LocationGeoIpProvider(diskProvider, appFolderInfo);
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.Should().HaveFlag(GeoIpCapabilities.Country);

        // 1. IPv4 Lookups
        var usResult = await provider.LookupAsync("8.8.8.8");
        usResult.Should().NotBeNull();
        usResult.CountryCode.Should().Be("US");
        usResult.CountryName.Should().Be("United States");

        var mappedResult = await provider.LookupAsync("::ffff:8.8.8.8");
        mappedResult.Should().NotBeNull();
        mappedResult.CountryCode.Should().Be("US");

        // 2. IPv6 Lookups
        var gbV6Result = await provider.LookupAsync("2001:db8:1::42");
        gbV6Result.Should().NotBeNull();
        gbV6Result.CountryCode.Should().Be("GB");

        // 3. Invalid inputs
        var nullResult = await provider.LookupAsync(null!);
        nullResult.Should().BeNull();

        var emptyResult = await provider.LookupAsync("   ");
        emptyResult.Should().BeNull();

        var invalidIpResult = await provider.LookupAsync("999.999.999.999");
        invalidIpResult.Should().NotBeNull();
        invalidIpResult.CountryCode.Should().BeNull();
    }

    private static void CreateSampleBinaryDatabase(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        const uint baseAddrIPv4 = 22;
        const uint baseAddrIPv6 = 38;
        const uint countryOffsetUS = 77;
        const uint countryOffsetGB = 94;

        // Header: 21 bytes
        writer.Write((byte)1); // dbType
        writer.Write((byte)2); // dbColumn (IP From + Country)
        writer.Write((byte)26); // year
        writer.Write((byte)9); // month
        writer.Write((byte)5); // day
        writer.Write(2u); // ipv4Count
        writer.Write(baseAddrIPv4); // baseAddress
        writer.Write(2u); // ipv6Count
        writer.Write(baseAddrIPv6); // baseAddressIPv6

        // IPv4 Record 0: 0.0.0.0 -> US
        var ipBytes1 = IPAddress.Parse("0.0.0.0").GetAddressBytes();
        Array.Reverse(ipBytes1);
        writer.Write(BitConverter.ToUInt32(ipBytes1, 0));
        writer.Write(countryOffsetUS);

        // IPv4 Record 1: 9.0.0.0 -> GB
        var ipBytes2 = IPAddress.Parse("9.0.0.0").GetAddressBytes();
        Array.Reverse(ipBytes2);
        writer.Write(BitConverter.ToUInt32(ipBytes2, 0));
        writer.Write(countryOffsetGB);

        // IPv6 Record 0: :: -> US
        var ipv6Bytes0 = new byte[16];
        writer.Write(ipv6Bytes0);
        writer.Write(countryOffsetUS);

        // IPv6 Record 1: 2001:db8:1:: -> GB
        var ipv6Addr1 = IPAddress.Parse("2001:db8:1::").GetAddressBytes();
        var ip1BigInt = new BigInteger(ipv6Addr1, isUnsigned: true, isBigEndian: true);
        var ip1LeBytes = new byte[16];
        var exported = ip1BigInt.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Copy(exported, ip1LeBytes, Math.Min(exported.Length, 16));
        writer.Write(ip1LeBytes);
        writer.Write(countryOffsetGB);

        // Country Strings (offset 77)
        var usCode = Encoding.ASCII.GetBytes("US");
        var usName = Encoding.ASCII.GetBytes("United States");
        writer.Write((byte)usCode.Length);
        writer.Write(usCode);
        writer.Write((byte)usName.Length);
        writer.Write(usName);

        // Country Strings (offset 94)
        var gbCode = Encoding.ASCII.GetBytes("GB");
        var gbName = Encoding.ASCII.GetBytes("United Kingdom");
        writer.Write((byte)gbCode.Length);
        writer.Write(gbCode);
        writer.Write((byte)gbName.Length);
        writer.Write(gbName);
    }
}
