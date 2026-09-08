// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network.GeoIp;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class GeoIpUpdateTaskTest
{
    private IConfigService configService = null!;
    private IDiskProvider diskProvider = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private MaxMindGeoIpProvider maxMindProvider = null!;
    private string tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), $"geoip_task_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);

        this.configService = Substitute.For<IConfigService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.appFolderInfo.AppDataFolder.Returns(this.tempDir);

        this.maxMindProvider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        this.maxMindProvider?.Dispose();
        if (Directory.Exists(this.tempDir))
        {
            try
            {
                Directory.Delete(this.tempDir, recursive: true);
            }
            catch
            {
                // Ignored in cleanup
            }
        }
    }

    [Test]
    public void ExtractMmdbBytes_WithTarGzArchive_ExtractsMmdbFileCorrectly()
    {
        var sampleMmdb = CreateDummyMmdbBytes();
        var tarGzData = CreateSampleTarGz("GeoLite2-City_20260908/GeoLite2-City.mmdb", sampleMmdb);

        var extracted = GeoIpUpdateTask.ExtractMmdbBytes(tarGzData);

        extracted.Should().NotBeNull();
        extracted.Should().BeEquivalentTo(sampleMmdb);
    }

    [Test]
    public void ExtractMmdbBytes_WithZipArchive_ExtractsMmdbFileCorrectly()
    {
        var sampleMmdb = CreateDummyMmdbBytes();
        var zipData = CreateSampleZip("GeoLite2-City.mmdb", sampleMmdb);

        var extracted = GeoIpUpdateTask.ExtractMmdbBytes(zipData);

        extracted.Should().NotBeNull();
        extracted.Should().BeEquivalentTo(sampleMmdb);
    }

    [Test]
    public void ExtractMmdbBytes_WithRawMmdbBytes_ReturnsOriginalData()
    {
        var sampleMmdb = CreateDummyMmdbBytes();

        var extracted = GeoIpUpdateTask.ExtractMmdbBytes(sampleMmdb);

        extracted.Should().NotBeNull();
        extracted.Should().BeEquivalentTo(sampleMmdb);
    }

    [Test]
    public async Task ExecuteAsync_WhenDownloadSucceeds_AtomicallyInstallsDatabaseAndReloadsProvider()
    {
        var sampleMmdb = CreateDummyMmdbBytes();
        var tarGzData = CreateSampleTarGz("GeoLite2-City.mmdb", sampleMmdb);

        this.safeHttpClientService.DownloadBytesAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(tarGzData);

        var task = new GeoIpUpdateTask(
            this.configService,
            this.diskProvider,
            this.appFolderInfo,
            new IGeoIpProvider[] { this.maxMindProvider },
            this.safeHttpClientService);

        var result = await task.ExecuteAsync();

        result.Should().BeTrue();

        var expectedDest = Path.Combine(this.tempDir, "GeoIP", "GeoLite2-City.mmdb");
        File.Exists(expectedDest).Should().BeTrue();
        var writtenBytes = await File.ReadAllBytesAsync(expectedDest);
        writtenBytes.Should().BeEquivalentTo(sampleMmdb);
    }

    [Test]
    public async Task ExecuteAsync_WhenDownloadFails_ReturnsFalseWithoutThrowing()
    {
        this.safeHttpClientService.DownloadBytesAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]>(new HttpRequestException("Download error")));

        var task = new GeoIpUpdateTask(
            this.configService,
            this.diskProvider,
            this.appFolderInfo,
            new IGeoIpProvider[] { this.maxMindProvider },
            this.safeHttpClientService);

        var result = await task.ExecuteAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteAsync_WhenDownloadedPayloadIsNotValidMmdb_ReturnsFalse()
    {
        var invalidData = Encoding.UTF8.GetBytes("<html><body>Error 404 Not Found</body></html>");

        this.safeHttpClientService.DownloadBytesAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(invalidData);

        var task = new GeoIpUpdateTask(
            this.configService,
            this.diskProvider,
            this.appFolderInfo,
            new IGeoIpProvider[] { this.maxMindProvider },
            this.safeHttpClientService);

        var result = await task.ExecuteAsync();

        result.Should().BeFalse();
    }

    private static byte[] CreateDummyMmdbBytes()
    {
        var bytes = new byte[2048];
        new Random(42).NextBytes(bytes);

        // Append MaxMind metadata marker
        var marker = Encoding.ASCII.GetBytes("\xAB\xCD\xEFMaxMind.Db");
        Array.Copy(marker, 0, bytes, bytes.Length - marker.Length, marker.Length);
        return bytes;
    }

    private static byte[] CreateSampleTarGz(string entryName, byte[] entryContent)
    {
        using var uncompressedTar = new MemoryStream();
        using (var tarWriter = new TarWriter(uncompressedTar, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = new MemoryStream(entryContent),
            };
            tarWriter.WriteEntry(entry);
        }

        uncompressedTar.Position = 0;

        using var outputGz = new MemoryStream();
        using (var gzStream = new GZipStream(outputGz, CompressionMode.Compress, leaveOpen: true))
        {
            uncompressedTar.CopyTo(gzStream);
        }

        return outputGz.ToArray();
    }

    private static byte[] CreateSampleZip(string entryName, byte[] entryContent)
    {
        using var memStream = new MemoryStream();
        using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(entryContent, 0, entryContent.Length);
        }

        return memStream.ToArray();
    }
}
