// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Packages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Packages;

[TestFixture]
public class PackageManagementTest
{
    private ITorrentService torrentService = null!;
    private ITagRepository tagRepository = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IConfigService configService = null!;
    private IPackageExportService packageExportService = null!;
    private IPackageImportService packageImportService = null!;
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "LeecharrPackageTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);

        this.torrentService = Substitute.For<ITorrentService>();
        this.tagRepository = Substitute.For<ITagRepository>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.tempDirectory);

        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configService = Substitute.For<IConfigService>();
        this.configService.DownloadDir.Returns(Path.Combine(this.tempDirectory, "downloads"));

        this.packageExportService = new PackageExportService(
            this.torrentService,
            this.tagRepository,
            this.appFolderInfo);

        this.packageImportService = new PackageImportService(
            this.torrentService,
            this.torrentFileParser,
            this.appFolderInfo,
            this.configService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task ExportPackageAsync_ArgumentValidation_ThrowsAppropriateExceptions()
    {
        using var stream = new MemoryStream();

        var actNullStream = async () => await this.packageExportService.ExportPackageAsync(null!, new[] { 1 }, false);
        await actNullStream.Should().ThrowAsync<ArgumentNullException>();

        var actNullIds = async () => await this.packageExportService.ExportPackageAsync(stream, null!, false);
        await actNullIds.Should().ThrowAsync<ArgumentNullException>();

        var actEmptyIds = async () => await this.packageExportService.ExportPackageAsync(stream, Array.Empty<int>(), false);
        await actEmptyIds.Should().ThrowAsync<ArgumentException>().WithMessage("*torrent ID*");

        this.torrentService.Get(Arg.Any<int>()).Returns((Torrent)null!);
        var actMissingTorrents = async () => await this.packageExportService.ExportPackageAsync(stream, new[] { 99 }, false);
        await actMissingTorrents.Should().ThrowAsync<ArgumentException>().WithMessage("*None of the specified torrents were found*");
    }

    [Test]
    public async Task ExportPackageAsync_SingleTorrent_WritesManifestAndFastResumeEntries()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Ubuntu 24.04 LTS",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Category = "Linux ISO",
            TrackerUrl = "udp://tracker.opentrackr.org:1337/announce",
            TotalSize = 4000000000,
            PieceCount = 2000,
            PieceLength = 2097152,
            TagIds = new List<int> { 101, 102 },
            SavePath = Path.Combine(this.tempDirectory, "ubuntu.iso"),
            Status = TorrentStatus.Seeding,
        };

        this.torrentService.Get(1).Returns(torrent);

        this.tagRepository.All().Returns(new List<Tag>
        {
            new Tag { Id = 101, Label = "Ubuntu" },
            new Tag { Id = 102, Label = "Distro" },
        });

        using var memoryStream = new MemoryStream();
        await this.packageExportService.ExportPackageAsync(memoryStream, new[] { 1 }, includePayload: false);

        memoryStream.Position = 0;
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        PackageManifest? manifest = null;
        var entriesFound = new List<string>();

        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            entriesFound.Add(entry.Name);
            if (entry.Name == "manifest.json" && entry.DataStream != null)
            {
                using var ms = new MemoryStream();
                await entry.DataStream.CopyToAsync(ms);
                var json = Encoding.UTF8.GetString(ms.ToArray());
                manifest = System.Text.Json.JsonSerializer.Deserialize<PackageManifest>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        entriesFound.Should().Contain("manifest.json");
        entriesFound.Should().Contain("fastresume/0123456789abcdef0123456789abcdef01234567.json");

        manifest.Should().NotBeNull();
        manifest!.SchemaVersion.Should().Be(1);
        manifest.Client.Should().Be("Leecharr");
        manifest.Torrents.Should().HaveCount(1);

        var item = manifest.Torrents[0];
        item.Name.Should().Be("Ubuntu 24.04 LTS");
        item.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        item.Category.Should().Be("Linux ISO");
        item.Tags.Should().Contain("Ubuntu");
        item.Tags.Should().Contain("Distro");
        item.Trackers.Should().ContainSingle(t => t.Url == "udp://tracker.opentrackr.org:1337/announce");
    }

    [Test]
    public async Task ExportPackageAsync_WithMetainfoAndPayload_IncludesFilesAndTarStructure()
    {
        var infoHash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrentsDir = Path.Combine(this.tempDirectory, "torrents");
        Directory.CreateDirectory(torrentsDir);
        var torrentFilePath = Path.Combine(torrentsDir, $"{infoHash}.torrent");
        await File.WriteAllBytesAsync(torrentFilePath, new byte[] { 0x64, 0x38, 0x3A, 0x61, 0x6E, 0x6E, 0x6F, 0x75, 0x6E, 0x63, 0x65, 0x65 });

        var payloadDir = Path.Combine(this.tempDirectory, "payload_test");
        Directory.CreateDirectory(payloadDir);
        var payloadSubFile = Path.Combine(payloadDir, "subfolder", "data.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadSubFile)!);
        await File.WriteAllBytesAsync(payloadSubFile, new byte[] { 1, 2, 3, 4, 5 });

        var torrent = new Torrent
        {
            Id = 2,
            Name = "Payload Test Torrent",
            InfoHash = infoHash,
            SavePath = payloadDir,
        };
        this.torrentService.Get(2).Returns(torrent);

        using var memoryStream = new MemoryStream();
        await this.packageExportService.ExportPackageAsync(memoryStream, new[] { 2 }, includePayload: true);

        memoryStream.Position = 0;
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        var entryNames = new List<string>();
        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            entryNames.Add(entry.Name.Replace('\\', '/'));
        }

        entryNames.Should().Contain("manifest.json");
        entryNames.Should().Contain($"metainfo/{infoHash}.torrent");
        entryNames.Should().Contain($"fastresume/{infoHash}.json");
        entryNames.Should().Contain(n => n.StartsWith($"payload/{infoHash}/subfolder/data.bin"));
    }

    [Test]
    public async Task ImportPackageAsync_Roundtrip_ExportsAndImportsSuccessfully()
    {
        var infoHash = "1234567890abcdef1234567890abcdef12345678";
        var torrentsDir = Path.Combine(this.tempDirectory, "torrents");
        Directory.CreateDirectory(torrentsDir);
        var torrentFilePath = Path.Combine(torrentsDir, $"{infoHash}.torrent");
        await File.WriteAllBytesAsync(torrentFilePath, Encoding.UTF8.GetBytes("d8:announce3:udpe"));

        var originalTorrent = new Torrent
        {
            Id = 10,
            Name = "Import Roundtrip Torrent",
            InfoHash = infoHash,
            Category = "Media",
            TotalSize = 1024,
        };
        this.torrentService.Get(10).Returns(originalTorrent);

        using var packageStream = new MemoryStream();
        await this.packageExportService.ExportPackageAsync(packageStream, new[] { 10 }, includePayload: false);
        packageStream.Position = 0;

        // Reset mocks for import verification
        this.torrentService.GetByInfoHash(infoHash).Returns((Torrent)null!);
        var parsedMock = new ParsedTorrent
        {
            Name = "Import Roundtrip Torrent",
            InfoHash = infoHash,
            TotalSize = 1024,
        };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedMock);

        var restoredTorrent = new Torrent
        {
            Id = 42,
            Name = "Import Roundtrip Torrent",
            InfoHash = infoHash,
            Category = "Media",
            TotalSize = 1024,
            Status = TorrentStatus.Downloading,
        };
        this.torrentService.AddFromParsedTorrentAsync(parsedMock, "Media", Arg.Any<string>(), false, Arg.Any<byte[]>())
            .Returns(Task.FromResult(restoredTorrent));

        var options = new PackageImportOptions
        {
            RestoreTorrents = true,
            SkipDuplicates = true,
        };

        var result = await this.packageImportService.ImportPackageAsync(packageStream, options);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ImportedTorrentsCount.Should().Be(1);
        result.SkippedDuplicatesCount.Should().Be(0);
        result.Torrents.Should().ContainSingle(t => t.Id == 42 && t.InfoHash == infoHash && !t.IsDuplicate);
    }

    [Test]
    public async Task ImportPackageAsync_DuplicateTorrent_SkipsAdditionAndMarksDuplicate()
    {
        var infoHash = "ffffffffffffffffffffffffffffffffffffffff";
        var existingTorrent = new Torrent
        {
            Id = 99,
            Name = "Existing Duplicate",
            InfoHash = infoHash,
            Category = "Software",
            TotalSize = 2048,
            Status = TorrentStatus.Seeding,
        };
        this.torrentService.GetByInfoHash(infoHash).Returns(existingTorrent);

        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new PackageManifest
            {
                Torrents = new List<PackageTorrentItem>
                {
                    new PackageTorrentItem { Id = 1, Name = "Existing Duplicate", InfoHash = infoHash },
                },
            };
            var jsonBytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(manifest));
            using var manifestMs = new MemoryStream(jsonBytes);
            await tarWriter.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json") { DataStream = manifestMs });
        }

        memoryStream.Position = 0;
        var result = await this.packageImportService.ImportPackageAsync(memoryStream);

        result.Should().NotBeNull();
        result.SkippedDuplicatesCount.Should().Be(1);
        result.SkippedDuplicates.Should().Contain(infoHash);
        result.ImportedTorrentsCount.Should().Be(0);
        await this.torrentService.DidNotReceiveWithAnyArgs().AddFromParsedTorrentAsync(default!);
    }

    [Test]
    public async Task ImportPackageAsync_ZipSlipAttack_ThrowsSecurityException()
    {
        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var maliciousPayload = Encoding.UTF8.GetBytes("malicious content");
            using var payloadMs = new MemoryStream(maliciousPayload);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "payload/hash/../../etc/passwd")
            {
                DataStream = payloadMs,
            };
            await tarWriter.WriteEntryAsync(entry);
        }

        memoryStream.Position = 0;
        var act = async () => await this.packageImportService.ImportPackageAsync(memoryStream);
        await act.Should().ThrowAsync<SecurityException>().WithMessage("*Zip-Slip*");
    }

    [Test]
    public async Task ImportPackageAsync_ExceedsMaxUncompressedBytes_ThrowsInvalidOperationException()
    {
        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var payloadData = new byte[1024];
            using var payloadMs = new MemoryStream(payloadData);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "payload/0123456789abcdef0123456789abcdef01234567/large.bin")
            {
                DataStream = payloadMs,
            };
            await tarWriter.WriteEntryAsync(entry);
        }

        memoryStream.Position = 0;
        var options = new PackageImportOptions
        {
            MaxUncompressedBytes = 500, // lower than 1024
        };

        var act = async () => await this.packageImportService.ImportPackageAsync(memoryStream, options);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds maximum extraction limit*");
    }

    [Test]
    public async Task ImportPackageAsync_ExceedsMaxCompressionRatio_ThrowsInvalidOperationException()
    {
        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            // Highly compressible payload of zeros: 2 MiB
            var payloadData = new byte[2 * 1024 * 1024];
            using var payloadMs = new MemoryStream(payloadData);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "payload/0123456789abcdef0123456789abcdef01234567/zero.bin")
            {
                DataStream = payloadMs,
            };
            await tarWriter.WriteEntryAsync(entry);
        }

        memoryStream.Position = 0;
        var options = new PackageImportOptions
        {
            MaxCompressionRatio = 5.0,
            MinBytesForRatioCheck = 1024 * 1024, // 1 MiB
        };

        var act = async () => await this.packageImportService.ImportPackageAsync(memoryStream, options);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Suspicious compression ratio*");
    }

    [Test]
    public async Task ImportPackageAsync_UncompressedTarArchive_ProcessesWithoutGzipHeader()
    {
        using var memoryStream = new MemoryStream();
        await using (var tarWriter = new TarWriter(memoryStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new PackageManifest
            {
                Torrents = new List<PackageTorrentItem>(),
            };
            var jsonBytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(manifest));
            using var manifestMs = new MemoryStream(jsonBytes);
            await tarWriter.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json") { DataStream = manifestMs });
        }

        memoryStream.Position = 0;
        var result = await this.packageImportService.ImportPackageAsync(memoryStream);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Test]
    public void PackagePathTranslator_ValidatesZipSlip_AndTranslatesPaths()
    {
        var actValid1 = () => PackagePathTranslator.ValidateZipSlip("data/file.txt");
        var actValid2 = () => PackagePathTranslator.ValidateZipSlip("a/b/c/d.mkv");
        actValid1.Should().NotThrow();
        actValid2.Should().NotThrow();

        var actMalicious1 = () => PackagePathTranslator.ValidateZipSlip("../secret.key");
        var actMalicious2 = () => PackagePathTranslator.ValidateZipSlip("folder/../../etc/shadow");
        actMalicious1.Should().Throw<SecurityException>().WithMessage("*Zip-Slip*");
        actMalicious2.Should().Throw<SecurityException>().WithMessage("*Zip-Slip*");

        PackagePathTranslator.NormalizePathSeparators("foo\\bar/baz", '/').Should().Be("foo/bar/baz");
        PackagePathTranslator.StripDriveLetter("C:\\Torrents\\Linux").Should().Be("\\Torrents\\Linux");

        var remappings = new Dictionary<string, string>
        {
            ["/data/torrents"] = "/mnt/storage/media",
        };
        var translated = PackagePathTranslator.TranslatePath(
            "/data/torrents/movies/test.mkv",
            remappings: remappings,
            targetSeparator: '/');

        translated.Should().Be("/mnt/storage/media/movies/test.mkv");
    }

    [Test]
    public async Task PackageController_Export_Get_ReturnsFileResultWithSanitizedFilename()
    {
        var torrent = new Torrent
        {
            Id = 5,
            Name = "Debian 12: Bookworm (Official)",
        };
        this.torrentService.Get(5).Returns(torrent);

        var controller = new PackageController(this.packageExportService, this.torrentService);
        var result = await controller.Export("5", false);

        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should().Be("application/x-leecharr-package");
        fileResult.FileDownloadName.Should().Be("Debian 12 Bookworm (Official).leecharr");
    }

    [Test]
    public async Task PackageController_Export_MultipleTorrents_ReturnsGenericFilename()
    {
        var t1 = new Torrent { Id = 1, Name = "Torrent 1" };
        var t2 = new Torrent { Id = 2, Name = "Torrent 2" };
        this.torrentService.Get(1).Returns(t1);
        this.torrentService.Get(2).Returns(t2);

        var controller = new PackageController(this.packageExportService, this.torrentService);
        var result = await controller.Export("1,2", false);

        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.FileDownloadName.Should().Be("package.leecharr");
    }

    [Test]
    public async Task PackageController_Export_ValidationErrors_ReturnsExpectedStatusCodes()
    {
        var controller = new PackageController(this.packageExportService, this.torrentService);

        var badRequestResult = await controller.Export(string.Empty, false);
        badRequestResult.Should().BeOfType<BadRequestObjectResult>();

        this.torrentService.Get(Arg.Any<int>()).Returns((Torrent)null!);
        var notFoundResult = await controller.Export("999", false);
        notFoundResult.Should().BeOfType<NotFoundObjectResult>();

        var nullServiceController = new PackageController(null!, this.torrentService);
        var notImplementedResult = await nullServiceController.Export("1", false);
        var statusResult = notImplementedResult.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Test]
    public async Task PackageController_ExportPost_WithRequestBody_ExecutesSuccessfully()
    {
        var torrent = new Torrent { Id = 7, Name = "Export Post Torrent" };
        this.torrentService.Get(7).Returns(torrent);

        var controller = new PackageController(this.packageExportService, this.torrentService);
        var request = new PackageExportRequest
        {
            TorrentIds = new List<int> { 7 },
            IncludePayload = false,
        };

        var result = await controller.ExportPost(request);
        result.Should().BeOfType<FileStreamResult>();
    }

    [Test]
    public async Task PackageController_Import_ValidationAndErrorHandling()
    {
        var controller = new PackageController(this.packageExportService, this.torrentService, this.packageImportService);

        // 1. Missing file -> BadRequest
        var noFileResult = await controller.Import(file: null);
        noFileResult.Should().BeOfType<BadRequestObjectResult>();

        var emptyFormFile = Substitute.For<IFormFile>();
        emptyFormFile.Length.Returns(0);
        var emptyFileResult = await controller.Import(file: emptyFormFile);
        emptyFileResult.Should().BeOfType<BadRequestObjectResult>();

        // 2. Null import service -> 501
        var nullImportController = new PackageController(this.packageExportService, this.torrentService, null);
        var notImplResult = await nullImportController.Import(file: emptyFormFile);
        var notImplObj = notImplResult.Should().BeOfType<ObjectResult>().Subject;
        notImplObj.StatusCode.Should().Be(StatusCodes.Status501NotImplemented);

        // 3. SecurityException -> 400
        var mockImportService = Substitute.For<IPackageImportService>();
        mockImportService.ImportPackageAsync(Arg.Any<Stream>(), Arg.Any<PackageImportOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SecurityException("Malicious path"));

        var validFile = Substitute.For<IFormFile>();
        validFile.Length.Returns(100);
        validFile.OpenReadStream().Returns(new MemoryStream(new byte[100]));

        var secController = new PackageController(this.packageExportService, this.torrentService, mockImportService);
        var secResult = await secController.Import(file: validFile);
        var badReq = secResult.Should().BeOfType<BadRequestObjectResult>().Subject;
        badReq.Value.ToString().Should().Contain("Security violation");

        // 4. InvalidOperationException -> 400
        mockImportService.ImportPackageAsync(Arg.Any<Stream>(), Arg.Any<PackageImportOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Payload too large"));

        validFile.OpenReadStream().Returns(new MemoryStream(new byte[100]));
        var invOpResult = await secController.Import(file: validFile);
        invOpResult.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void DeterminePackageFileName_SanitizesSpecialCharactersCorrectly()
    {
        PackageController.DeterminePackageFileName(null).Should().Be("package.leecharr");
        PackageController.DeterminePackageFileName(Array.Empty<Torrent>()).Should().Be("package.leecharr");

        var clean = new[] { new Torrent { Name = "CleanTorrentName" } };
        PackageController.DeterminePackageFileName(clean).Should().Be("CleanTorrentName.leecharr");

        var special = new[] { new Torrent { Name = "Dirty: Name <with> \"invalid\" | chars?*" } };
        PackageController.DeterminePackageFileName(special).Should().Be("Dirty Name with invalid  chars.leecharr");

        var multi = new[] { new Torrent { Name = "Torrent1" }, new Torrent { Name = "Torrent2" } };
        PackageController.DeterminePackageFileName(multi).Should().Be("package.leecharr");
    }
}
