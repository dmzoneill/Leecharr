// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class PackageImportServiceComprehensiveIntegrationTests : IntegrationTestBase
{
    private string tempSandboxDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempSandboxDir = Path.Combine(Path.GetTempPath(), "package_sandbox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempSandboxDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempSandboxDir))
        {
            try
            {
                Directory.Delete(this.tempSandboxDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task PackageImport_DuplicateTorrent_SkipsCleanlyWithoutExtraction()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentService = (ITorrentService)services.GetService(typeof(ITorrentService))!;
        var importService = new PackageImportService(torrentService);

        const string duplicateHash = "1234567890123456789012345678901234567890";

        // Create an in-memory .tar.gz package
        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new PackageManifest
            {
                SchemaVersion = 1,
                Torrents = new List<PackageTorrentItem>
                {
                    new() { Id = 1, Name = "SkippedDuplicateMovie", InfoHash = duplicateHash },
                },
            };

            var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            using var manifestMs = new MemoryStream(manifestBytes);
            await tarWriter.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = manifestMs,
            });
        }

        memoryStream.Position = 0;
        var options = new PackageImportOptions
        {
            TargetRootDir = this.tempSandboxDir,
        };

        var result = await importService.ImportPackageAsync(memoryStream, options);
        result.Should().NotBeNull();
        result.ImportedTorrentsCount.Should().Be(0);
    }

    [Test]
    public async Task PackageImport_ZipSlipAttack_ThrowsSecurityException()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentService = (ITorrentService)services.GetService(typeof(ITorrentService))!;
        var importService = new PackageImportService(torrentService);

        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var maliciousPayload = Encoding.UTF8.GetBytes("malicious script contents");
            using var payloadMs = new MemoryStream(maliciousPayload);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "payload/../../../../etc/passwd")
            {
                DataStream = payloadMs,
            };
            await tarWriter.WriteEntryAsync(entry);
        }

        memoryStream.Position = 0;
        var act = async () => await importService.ImportPackageAsync(memoryStream, new PackageImportOptions
        {
            TargetRootDir = this.tempSandboxDir,
        });

        await act.Should().ThrowAsync<SecurityException>();
    }

    [Test]
    public async Task PackageImport_UncompressedTarPackage_ImportsSuccessfully()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentService = (ITorrentService)services.GetService(typeof(ITorrentService))!;
        var importService = new PackageImportService(torrentService);

        // Test uncompressed tar archive without gzip header
        using var memoryStream = new MemoryStream();
        await using (var tarWriter = new TarWriter(memoryStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new PackageManifest
            {
                SchemaVersion = 1,
                Torrents = new List<PackageTorrentItem>(),
            };

            var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            using var manifestMs = new MemoryStream(manifestBytes);
            await tarWriter.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = manifestMs,
            });
        }

        memoryStream.Position = 0;
        var result = await importService.ImportPackageAsync(memoryStream, new PackageImportOptions
        {
            TargetRootDir = this.tempSandboxDir,
        });

        result.Should().NotBeNull();
        result.ImportedTorrentsCount.Should().Be(0);
    }
}
