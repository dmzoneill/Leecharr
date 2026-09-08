// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Transmission;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class TransmissionRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IDiskSpaceService diskSpaceService = null!;
    private IDiskProvider diskProvider = null!;
    private IDownloadEngine downloadEngine = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IBlocklistUpdateService blocklistUpdateService = null!;
    private IBlocklistService blocklistService = null!;
    private TransmissionRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.diskSpaceService = Substitute.For<IDiskSpaceService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.blocklistUpdateService = Substitute.For<IBlocklistUpdateService>();
        this.blocklistService = Substitute.For<IBlocklistService>();

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");

        this.torrentService.GetDownloadTask(Arg.Any<int>()).Returns(x => this.downloadEngine.GetTask(x.Arg<int>()));

        this.controller = new TransmissionRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.configService,
            diskSpaceService: this.diskSpaceService,
            configFileProvider: this.configFileProvider,
            diskProvider: this.diskProvider,
            downloadEngine: this.downloadEngine,
            trackerEntryRepository: this.trackerEntryRepository,
            blocklistUpdateService: this.blocklistUpdateService,
            blocklistService: this.blocklistService);
    }

    [Test]
    public void HandleGet_WhenUnauthenticated_Returns401Unauthorized()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = this.controller.HandleGet();

        result.Should().BeOfType<UnauthorizedResult>();
        context.Response.Headers["WWW-Authenticate"].ToString().Should().Contain("Transmission");
    }

    [Test]
    public void HandleGet_WithValidApiKeyHeader_Succeeds()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "secret_api_key_123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = this.controller.HandleGet();

        // Without transmission session header, transmission protocol responds 409 Conflict with session header
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Headers.ContainsKey("X-Transmission-Session-Id").Should().BeTrue();
    }

    [Test]
    public async Task HandleRpc_WhenUnauthenticated_Returns401Unauthorized()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest { Method = "session-get" });

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task HandleRpc_WithBasicAuthMatchingApiKey_Succeeds()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest { Method = "session-get" });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task HandleRpc_TorrentGet_WithoutFilesField_DoesNotQueryFileService()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Transmission.Test",
            InfoHash = "1122334455667788990011223344556677889900",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent> { torrent });

        var args = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
        using var doc = System.Text.Json.JsonDocument.Parse("[\"id\", \"name\", \"status\"]");
        args["fields"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        this.torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    [Test]
    public async Task HandleRpc_TorrentGet_WithFilesField_QueriesFileService()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Transmission.Test",
            InfoHash = "1122334455667788990011223344556677889900",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent> { torrent });
        this.torrentFileService.GetFiles(42).Returns(new System.Collections.Generic.List<TorrentFile>());

        var args = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
        using var doc = System.Text.Json.JsonDocument.Parse("[\"id\", \"name\", \"files\"]");
        args["fields"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        this.torrentFileService.Received(1).GetFiles(42);
    }

    [Test]
    public async Task HandleRpc_TorrentGet_WithFiles_ReturnsEnrichedBytesCompleted()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Transmission.Test",
            InfoHash = "1122334455667788990011223344556677889900",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            PieceLength = 500,
            PieceCount = 4,
            TotalSize = 2000,
        };

        var task = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();
        task.PieceBitfield.Returns(new[] { true, true, false, false });
        task.PieceLength.Returns(500);

        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 42, Path = "file1.bin", Size = 1000, PieceOffset = 0, PieceCount = 2 },
            new() { Id = 2, TorrentId = 42, Path = "file2.bin", Size = 1000, PieceOffset = 2, PieceCount = 2 },
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });
        this.torrentService.GetDownloadTask(42).Returns(task);
        this.torrentFileService.GetFiles(42).Returns(files);

        var args = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse("[\"id\", \"name\", \"files\", \"fileStats\"]");
        args["fields"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<TransmissionRpcResponse>().Subject;
        var argsDict = response.Arguments as Dictionary<string, object>;
        var torrentsList = argsDict!["torrents"] as List<Dictionary<string, object>>;
        torrentsList.Should().HaveCount(1);

        var returnedFiles = torrentsList![0]["files"] as List<Dictionary<string, object>>;
        returnedFiles.Should().HaveCount(2);
        returnedFiles![0]["bytesCompleted"].Should().Be(1000L);
        returnedFiles[1]["bytesCompleted"].Should().Be(0L);

        var returnedStats = torrentsList[0]["fileStats"] as List<Dictionary<string, object>>;
        returnedStats.Should().HaveCount(2);
        returnedStats![0]["bytesCompleted"].Should().Be(1000L);
        returnedStats[1]["bytesCompleted"].Should().Be(0L);
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithLocation_InvokesSetLocationAsyncWithMoveTrue()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "TestTorrent",
            SavePath = "/downloads/initial",
        };
        this.torrentService.Get(42).Returns(torrent);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[42]");
        using var locDoc = JsonDocument.Parse("\"/downloads/target\"");
        args["ids"] = idsDoc.RootElement.Clone();
        args["location"] = locDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/target", true);
        await this.torrentService.Received(1).UpdateAsync(Arg.Is<Torrent>(t => t.Id == 42 && t.SavePath == "/downloads/target"));
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithSameLocation_DoesNotInvokeSetLocationAsync()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "TestTorrent",
            SavePath = "/downloads/same",
        };
        this.torrentService.Get(42).Returns(torrent);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[42]");
        using var locDoc = JsonDocument.Parse("\"/downloads/same\"");
        args["ids"] = idsDoc.RootElement.Clone();
        args["location"] = locDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.DidNotReceive().SetLocationAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleRpc_TorrentSetLocation_WithMoveTrue_InvokesSetLocationAsyncWithMoveTrue()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
        using var idsDoc = System.Text.Json.JsonDocument.Parse("[42]");
        using var locDoc = System.Text.Json.JsonDocument.Parse("\"/downloads/target\"");
        using var moveDoc = System.Text.Json.JsonDocument.Parse("true");
        args["ids"] = idsDoc.RootElement.Clone();
        args["location"] = locDoc.RootElement.Clone();
        args["move"] = moveDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set-location",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/target", true);
    }

    [Test]
    public async Task HandleRpc_TorrentSetLocation_WithMoveFalse_InvokesSetLocationAsyncWithMoveFalse()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
        using var idsDoc = System.Text.Json.JsonDocument.Parse("[42]");
        using var locDoc = System.Text.Json.JsonDocument.Parse("\"/downloads/target\"");
        using var moveDoc = System.Text.Json.JsonDocument.Parse("false");
        args["ids"] = idsDoc.RootElement.Clone();
        args["location"] = locDoc.RootElement.Clone();
        args["move"] = moveDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set-location",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/target", false);
    }

    [Test]
    public async Task HandleRpc_TorrentSetLocation_WithoutMoveSpecified_DefaultsToMoveTrue()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
        using var idsDoc = System.Text.Json.JsonDocument.Parse("[42]");
        using var locDoc = System.Text.Json.JsonDocument.Parse("\"/downloads/target\"");
        args["ids"] = idsDoc.RootElement.Clone();
        args["location"] = locDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set-location",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/target", true);
    }

    [Test]
    public async Task HandleRpc_TorrentAdd_WithLocalFilePath_ParsesAndAddsTorrent()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tempFile = Path.Combine(Path.GetTempPath(), $"transmission_test_{Guid.NewGuid():N}.torrent");
        var fakeBytes = new byte[] { 0x64, 0x38, 0x3a, 0x61 };
        await File.WriteAllBytesAsync(tempFile, fakeBytes);

        try
        {
            var parsed = new ParsedTorrent
            {
                Name = "Ubuntu.24.04.iso",
                InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
                TotalSize = 1024,
            };
            this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
            this.torrentService.GetByInfoHash(parsed.InfoHash).Returns((Torrent)null);

            var added = new Torrent
            {
                Id = 101,
                Name = "Ubuntu.24.04.iso",
                InfoHash = parsed.InfoHash,
            };
            this.torrentService.AddFromParsedTorrentAsync(parsed, Arg.Any<string>(), Arg.Any<string>(), false, Arg.Any<byte[]>()).Returns(added);

            var args = new Dictionary<string, JsonElement>();
            using var fnDoc = JsonDocument.Parse($"\"{tempFile.Replace("\\", "\\\\")}\"");
            args["filename"] = fnDoc.RootElement.Clone();

            var result = await this.controller.HandleRpc(new TransmissionRpcRequest
            {
                Method = "torrent-add",
                Arguments = args,
            });

            result.Should().BeOfType<OkObjectResult>();
            var okResult = (OkObjectResult)result;
            var response = okResult.Value as TransmissionRpcResponse;
            response.Should().NotBeNull();
            response!.Result.Should().Be("success");

            var argsDict = response.Arguments as Dictionary<string, object>;
            argsDict.Should().NotBeNull();
            argsDict!.ContainsKey("torrent-added").Should().BeTrue();
            argsDict.ContainsKey("torrent-duplicate").Should().BeFalse();

            await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, Arg.Any<string>(), Arg.Any<string>(), false, Arg.Any<byte[]>());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task HandleRpc_TorrentAdd_WhenDuplicateTorrentSubmitted_ReturnsTorrentDuplicate()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tempFile = Path.Combine(Path.GetTempPath(), $"transmission_test_{Guid.NewGuid():N}.torrent");
        var fakeBytes = new byte[] { 0x64, 0x38, 0x3a, 0x61 };
        await File.WriteAllBytesAsync(tempFile, fakeBytes);

        try
        {
            var parsed = new ParsedTorrent
            {
                Name = "Existing.Release",
                InfoHash = "1111222233334444555566667777888899990000",
                TotalSize = 2048,
            };
            this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);

            var existingTorrent = new Torrent
            {
                Id = 77,
                Name = "Existing.Release",
                InfoHash = parsed.InfoHash,
            };
            this.torrentService.GetByInfoHash(parsed.InfoHash).Returns(existingTorrent);

            var args = new Dictionary<string, JsonElement>();
            using var fnDoc = JsonDocument.Parse($"\"{tempFile.Replace("\\", "\\\\")}\"");
            args["filename"] = fnDoc.RootElement.Clone();

            var result = await this.controller.HandleRpc(new TransmissionRpcRequest
            {
                Method = "torrent-add",
                Arguments = args,
            });

            result.Should().BeOfType<OkObjectResult>();
            var okResult = (OkObjectResult)result;
            var response = okResult.Value as TransmissionRpcResponse;
            response.Should().NotBeNull();
            response!.Result.Should().Be("success");

            var argsDict = response.Arguments as Dictionary<string, object>;
            argsDict.Should().NotBeNull();
            argsDict!.ContainsKey("torrent-duplicate").Should().BeTrue();
            argsDict.ContainsKey("torrent-added").Should().BeFalse();

            await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task HandleRpc_TorrentAdd_WhenNewTorrentSubmitted_ReturnsTorrentAdded()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var parsed = new ParsedTorrent
        {
            Name = "BrandNew.Release",
            InfoHash = "9999888877776666555544443333222211110000",
            TotalSize = 4096,
        };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.GetByInfoHash(parsed.InfoHash).Returns((Torrent)null);

        var newTorrent = new Torrent
        {
            Id = 88,
            Name = "BrandNew.Release",
            InfoHash = parsed.InfoHash,
        };
        this.torrentService.AddFromParsedTorrentAsync(parsed, Arg.Any<string>(), Arg.Any<string>(), false, Arg.Any<byte[]>()).Returns(newTorrent);

        var fakeBase64 = Convert.ToBase64String(new byte[] { 0x64, 0x38, 0x3a, 0x62 });
        var args = new Dictionary<string, JsonElement>();
        using var metaDoc = JsonDocument.Parse($"\"{fakeBase64}\"");
        args["metainfo"] = metaDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-add",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var argsDict = response.Arguments as Dictionary<string, object>;
        argsDict.Should().NotBeNull();
        argsDict!.ContainsKey("torrent-added").Should().BeTrue();
        argsDict.ContainsKey("torrent-duplicate").Should().BeFalse();
    }

    [Test]
    public async Task HandleRpc_FreeSpace_WithCustomPath_QueriesDiskProviderForPath()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var queriedPath = "/data/downloads";
        this.diskProvider.GetAvailableSpace(queriedPath).Returns(123456789L);
        this.diskProvider.GetTotalSize(queriedPath).Returns(987654321L);

        var args = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse($"\"{queriedPath}\"");
        args["path"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "free-space",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var argsDict = response.Arguments as Dictionary<string, object>;
        argsDict.Should().NotBeNull();
        argsDict!["path"].Should().Be(queriedPath);
        argsDict["size-bytes"].Should().Be(123456789L);
        argsDict["total_size"].Should().Be(987654321L);

        this.diskProvider.Received(1).GetAvailableSpace(queriedPath);
        this.diskProvider.Received(1).GetTotalSize(queriedPath);
    }

    [Test]
    public async Task HandleRpc_FreeSpace_WhenDiskProviderReturnsNull_FallsBackToDiskSpaceService()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var queriedPath = "/nonexistent/path";
        this.diskProvider.GetAvailableSpace(queriedPath).Returns((long?)null);
        this.diskProvider.GetTotalSize(queriedPath).Returns((long?)null);

        this.diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/",
                Label = "Root",
                FreeSpace = 555555555L,
                TotalSpace = 999999999L,
            },
        });

        var args = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse($"\"{queriedPath}\"");
        args["path"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "free-space",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var argsDict = response.Arguments as Dictionary<string, object>;
        argsDict.Should().NotBeNull();
        argsDict!["path"].Should().Be(queriedPath);
        argsDict["size-bytes"].Should().Be(555555555L);
        argsDict["total_size"].Should().Be(999999999L);

        this.diskProvider.Received(1).GetAvailableSpace(queriedPath);
        this.diskProvider.Received(1).GetTotalSize(queriedPath);
        this.diskSpaceService.Received().GetDiskSpace();
    }

    [Test]
    public async Task HandleRpc_FreeSpace_WhenDiskProviderThrows_FallsBackToDiskSpaceService()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var queriedPath = "/error/path";
        this.diskProvider.GetAvailableSpace(queriedPath).Returns(_ => throw new IOException("Disk error"));

        this.diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/",
                Label = "Root",
                FreeSpace = 444444444L,
                TotalSpace = 888888888L,
            },
        });

        var args = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse($"\"{queriedPath}\"");
        args["path"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "free-space",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var argsDict = response.Arguments as Dictionary<string, object>;
        argsDict.Should().NotBeNull();
        argsDict!["path"].Should().Be(queriedPath);
        argsDict["size-bytes"].Should().Be(444444444L);
        argsDict["total_size"].Should().Be(888888888L);
    }

    [Test]
    public async Task HandleRpc_FreeSpace_WithoutPath_UsesDownloadDir()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/configured/downloads");
        this.diskProvider.GetAvailableSpace("/configured/downloads").Returns(777777777L);
        this.diskProvider.GetTotalSize("/configured/downloads").Returns(888888888L);

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "free-space",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var argsDict = response.Arguments as Dictionary<string, object>;
        argsDict.Should().NotBeNull();
        argsDict!["path"].Should().Be("/configured/downloads");
        argsDict["size-bytes"].Should().Be(777777777L);
        argsDict["total_size"].Should().Be(888888888L);

        this.diskProvider.Received(1).GetAvailableSpace("/configured/downloads");
        this.diskProvider.Received(1).GetTotalSize("/configured/downloads");
    }

    [Test]
    public async Task HandleRpc_SessionGet_ReturnsAlternativeSpeedSettings()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.AlternativeSpeedEnabled.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(500);
        this.configService.AltUploadSpeedKbps.Returns(100);

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-get",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var args = response.Arguments as Dictionary<string, object>;
        args.Should().NotBeNull();
        args!["alt-speed-enabled"].Should().Be(true);
        args["alt-speed-down"].Should().Be(500);
        args["alt-speed-up"].Should().Be(100);
    }

    [Test]
    public async Task HandleRpc_SessionSet_SavesAlternativeSpeedEnabled()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse("true");
        args["alt-speed-enabled"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("AlternativeSpeedEnabled") && (bool)d["AlternativeSpeedEnabled"]));
    }

    [Test]
    public async Task HandleRpc_TorrentGet_WithRecentlyActive_ReturnsActiveAndRemoved()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var activeTorrent = new Torrent { Id = 10, Name = "ActiveTorrent", Status = TorrentStatus.Downloading, DownloadSpeed = 1000 };
        var stoppedTorrent = new Torrent { Id = 20, Name = "StoppedTorrent", Status = TorrentStatus.Stopped };
        this.torrentService.GetAll().Returns(new List<Torrent> { activeTorrent, stoppedTorrent });

        TransmissionRpcController.RecordRemovedId(99);

        var args = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse("\"recently-active\"");
        args["ids"] = doc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var responseArgs = response.Arguments as Dictionary<string, object>;
        responseArgs.Should().NotBeNull();
        responseArgs!.ContainsKey("removed").Should().BeTrue();
        var removed = responseArgs["removed"] as List<int>;
        removed.Should().Contain(99);

        var torrents = responseArgs["torrents"] as List<Dictionary<string, object>>;
        torrents.Should().NotBeNull();
        torrents!.Count.Should().Be(1);
        torrents[0]["id"].Should().Be(10);
    }

    [Test]
    public async Task HandleRpc_TorrentRenamePath_CallsRenameFileAsyncAndReturnsArguments()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[42]");
        args["ids"] = idsDoc.RootElement.Clone();
        using var pathDoc = JsonDocument.Parse("\"old/movie.mkv\"");
        args["path"] = pathDoc.RootElement.Clone();
        using var nameDoc = JsonDocument.Parse("\"new/movie.mkv\"");
        args["name"] = nameDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-rename-path",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var responseArgs = response.Arguments as Dictionary<string, object>;
        responseArgs.Should().NotBeNull();
        responseArgs!["path"].Should().Be("old/movie.mkv");
        responseArgs["name"].Should().Be("new/movie.mkv");
        responseArgs["id"].Should().Be(42);

        await this.torrentService.Received(1).RenameFileAsync(42, "old/movie.mkv", "new/movie.mkv");
    }

    [Test]
    public async Task HandleRpc_TorrentGet_QueuedTorrents_ReturnsCorrectStatusAndMetadata()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var queuedDownload = new Torrent { Id = 1, Name = "QueuedDL", Status = TorrentStatus.Queued, Progress = 0.5, DateAdded = DateTime.UtcNow, QueuePosition = 1 };
        var queuedSeed = new Torrent { Id = 2, Name = "QueuedSeed", Status = TorrentStatus.Queued, Progress = 1.0, DateAdded = DateTime.UtcNow, QueuePosition = 2, DateCompleted = DateTime.UtcNow };
        this.torrentService.GetAll().Returns(new List<Torrent> { queuedDownload, queuedSeed });

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();

        var responseArgs = response!.Arguments as Dictionary<string, object>;
        var torrents = responseArgs!["torrents"] as List<Dictionary<string, object>>;
        torrents.Should().NotBeNull();
        torrents!.Count.Should().Be(2);

        torrents[0]["status"].Should().Be(3); // TR_STATUS_DOWNLOAD_WAIT
        torrents[0]["queuePosition"].Should().Be(1);
        torrents[0].ContainsKey("addedDate").Should().BeTrue();

        torrents[1]["status"].Should().Be(5); // TR_STATUS_SEED_WAIT
        torrents[1]["queuePosition"].Should().Be(2);
        torrents[1].ContainsKey("doneDate").Should().BeTrue();
    }

    [Test]
    public async Task HandleRpc_SessionGet_ReturnsSeedRatioLimitAndLimited()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.GlobalSeedRatioLimit.Returns(2.5);
        this.configService.MaxDownloadSpeedKbps.Returns(500);
        this.configService.MaxUploadSpeedKbps.Returns(250);

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-get",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();

        var args = response!.Arguments as Dictionary<string, object>;
        args.Should().NotBeNull();
        args!["seedRatioLimit"].Should().Be(2.5);
        args["seedRatioLimited"].Should().Be(true);
        args["speed-limit-down-enabled"].Should().Be(true);
        args["speed-limit-up-enabled"].Should().Be(true);
    }

    [Test]
    public async Task HandleRpc_SessionSet_HandlesSpeedLimitTogglesAndSeedRatio()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new Dictionary<string, JsonElement>();
        using var speedDownDoc = JsonDocument.Parse("false");
        args["speed-limit-down-enabled"] = speedDownDoc.RootElement.Clone();
        using var speedUpDoc = JsonDocument.Parse("false");
        args["speed-limit-up-enabled"] = speedUpDoc.RootElement.Clone();
        using var ratioDoc = JsonDocument.Parse("1.5");
        args["seedRatioLimit"] = ratioDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["MaxDownloadSpeedKbps"] == 0 &&
            (int)d["MaxUploadSpeedKbps"] == 0 &&
            (double)d["GlobalSeedRatioLimit"] == 1.5));
    }

    [Test]
    public async Task HandleRpc_TorrentGet_IncludesRateLimitFields()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 5,
            Name = "LimitedTorrent",
            Status = TorrentStatus.Downloading,
            DownloadLimit = 1000,
            UploadLimit = 500,
            DateAdded = DateTime.UtcNow,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();

        var responseArgs = response!.Arguments as Dictionary<string, object>;
        var torrents = responseArgs!["torrents"] as List<Dictionary<string, object>>;
        torrents.Should().NotBeNull();
        torrents!.Count.Should().Be(1);

        torrents[0]["downloadLimit"].Should().Be(1000);
        torrents[0]["uploadLimit"].Should().Be(500);
        torrents[0]["downloadLimited"].Should().Be(true);
        torrents[0]["uploadLimited"].Should().Be(true);
        torrents[0]["honorsSessionLimits"].Should().Be(true);
    }

    [Test]
    public async Task HandleRpc_TorrentSet_HandlesDownloadLimitedAndUploadLimitedFalse()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 7,
            Name = "LimitTestTorrent",
            DownloadLimit = 1000,
            UploadLimit = 500,
        };
        this.torrentService.Get(7).Returns(torrent);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[7]");
        args["ids"] = idsDoc.RootElement.Clone();
        using var dlLimitedDoc = JsonDocument.Parse("false");
        args["downloadLimited"] = dlLimitedDoc.RootElement.Clone();
        using var ulLimitedDoc = JsonDocument.Parse("false");
        args["uploadLimited"] = ulLimitedDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        torrent.DownloadLimit.Should().Be(0);
        torrent.UploadLimit.Should().Be(0);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleRpc_TorrentGet_WithTrackersPeersPieces_ReturnsPopulatedFields()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";

        var trackerRepo = Substitute.For<NzbDrone.Core.Trackers.ITrackerEntryRepository>();
        var downloadEngine = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadEngine>();
        var downloadTask = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();

        trackerRepo.GetByTorrentId(1).Returns(new List<NzbDrone.Core.Trackers.TrackerEntry>
        {
            new()
            {
                Id = 1,
                TorrentId = 1,
                Url = "http://tracker.example.com/announce",
                Tier = 0,
                Seeders = 5,
                Leechers = 2,
                Downloaded = 100,
            },
        });

        downloadTask.GetPeers().Returns(new List<NzbDrone.Core.BitTorrent.PeerInfo>
        {
            new()
            {
                Ip = "10.0.0.5",
                Port = 51413,
                Client = "Transmission/3.00",
                DownloadSpeed = 500000,
                UploadSpeed = 100000,
                Progress = 0.8,
            },
        });
        downloadTask.PieceBitfield.Returns(new[] { true, true, false, true });
        downloadTask.PieceLength.Returns(524288);
        downloadEngine.GetTask(1).Returns(downloadTask);

        var controllerWithDeps = new TransmissionRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.configService,
            configFileProvider: this.configFileProvider,
            downloadEngine: downloadEngine,
            trackerEntryRepository: trackerRepo);
        controllerWithDeps.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 1,
            Name = "TransmissionTest",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            TotalSize = 2097152,
            PieceLength = 524288,
            PieceCount = 4,
            Progress = 0.75,
            DateAdded = DateTime.UtcNow,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var args = new Dictionary<string, JsonElement>();
        using var fieldsDoc = JsonDocument.Parse("[\"id\",\"name\",\"trackers\",\"trackerStats\",\"peers\",\"pieces\",\"pieceCount\",\"pieceSize\"]");
        args["fields"] = fieldsDoc.RootElement.Clone();

        var result = await controllerWithDeps.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        var responseArgs = response!.Arguments as Dictionary<string, object>;
        var torrents = responseArgs!["torrents"] as List<Dictionary<string, object>>;
        torrents.Should().NotBeNull();
        torrents!.Count.Should().Be(1);

        var t = torrents[0];
        t.Should().ContainKey("trackers");
        t.Should().ContainKey("trackerStats");
        t.Should().ContainKey("peers");
        t.Should().ContainKey("pieces");
        t.Should().ContainKey("pieceCount");
        t.Should().ContainKey("pieceSize");

        t["pieceCount"].Should().Be(4);
        t["pieceSize"].Should().Be(524288);

        var trackers = t["trackers"] as System.Collections.IEnumerable;
        trackers.Should().NotBeNull();

        var peers = t["peers"] as System.Collections.IEnumerable;
        peers.Should().NotBeNull();
    }

    [Test]
    public async Task HandleRpc_TorrentGet_WithNestedSavePath_ResolvesDownloadDirCorrectly()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 1,
            Name = "Spectre 2015",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            SavePath = "/downloads/Spectre 2015",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var args = new Dictionary<string, JsonElement>();
        using var fieldsDoc = JsonDocument.Parse("[\"id\",\"name\",\"downloadDir\"]");
        args["fields"] = fieldsDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        var responseArgs = response!.Arguments as Dictionary<string, object>;
        var torrents = responseArgs!["torrents"] as List<Dictionary<string, object>>;
        torrents.Should().NotBeNull();
        torrents!.Count.Should().Be(1);
        torrents[0]["downloadDir"].Should().Be("/downloads");
    }

    [Test]
    public async Task HandleRpc_SessionStats_ReturnsActivePausedSpeedAndNestedCumulativeAndCurrentStats()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Torrent 1", Status = TorrentStatus.Downloading, DownloadSpeed = 1024, UploadSpeed = 256, Downloaded = 5000, Uploaded = 1000 },
            new() { Id = 2, Name = "Torrent 2", Status = TorrentStatus.Seeding, DownloadSpeed = 0, UploadSpeed = 512, Downloaded = 10000, Uploaded = 20000 },
            new() { Id = 3, Name = "Torrent 3", Status = TorrentStatus.Paused, DownloadSpeed = 0, UploadSpeed = 0, Downloaded = 3000, Uploaded = 500 },
            new() { Id = 4, Name = "Torrent 4", Status = TorrentStatus.Stopped, DownloadSpeed = 0, UploadSpeed = 0, Downloaded = 2000, Uploaded = 0 },
            new() { Id = 5, Name = "Torrent 5", Status = TorrentStatus.Queued, DownloadSpeed = 0, UploadSpeed = 0, Downloaded = 0, Uploaded = 0 },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-stats",
            Tag = JsonDocument.Parse("99").RootElement,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var args = response.Arguments as Dictionary<string, object>;
        args.Should().NotBeNull();
        args!["activeTorrentCount"].Should().Be(2); // Downloading + Seeding
        args["pausedTorrentCount"].Should().Be(2); // Paused + Stopped
        args["torrentCount"].Should().Be(5);
        args["downloadSpeed"].Should().Be(1024L);
        args["uploadSpeed"].Should().Be(768L);

        var cumulative = args["cumulative-stats"] as Dictionary<string, object>;
        cumulative.Should().NotBeNull();
        cumulative!["downloadedBytes"].Should().Be(20000L);
        cumulative["uploadedBytes"].Should().Be(21500L);
        cumulative["filesAdded"].Should().Be(5);
        cumulative["sessionCount"].Should().Be(1);
        ((long)cumulative["secondsActive"]).Should().BeGreaterThanOrEqualTo(0);

        var current = args["current-stats"] as Dictionary<string, object>;
        current.Should().NotBeNull();
        current!["downloadedBytes"].Should().Be(20000L);
        current["uploadedBytes"].Should().Be(21500L);
        current["filesAdded"].Should().Be(5);
        current["sessionCount"].Should().Be(1);
        ((long)current["secondsActive"]).Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task HandleRpc_SessionStats_WhenNoTorrents_ReturnsZeroStats()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.torrentService.GetAll().Returns(new List<Torrent>());

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-stats",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var args = response.Arguments as Dictionary<string, object>;
        args.Should().NotBeNull();
        args!["activeTorrentCount"].Should().Be(0);
        args["pausedTorrentCount"].Should().Be(0);
        args["torrentCount"].Should().Be(0);
        args["downloadSpeed"].Should().Be(0L);
        args["uploadSpeed"].Should().Be(0L);

        var cumulative = args["cumulative-stats"] as Dictionary<string, object>;
        cumulative.Should().NotBeNull();
        cumulative!["downloadedBytes"].Should().Be(0L);
        cumulative["uploadedBytes"].Should().Be(0L);
        cumulative["filesAdded"].Should().Be(0);
        cumulative["sessionCount"].Should().Be(1);

        var current = args["current-stats"] as Dictionary<string, object>;
        current.Should().NotBeNull();
        current!["downloadedBytes"].Should().Be(0L);
        current["uploadedBytes"].Should().Be(0L);
        current["filesAdded"].Should().Be(0);
        current["sessionCount"].Should().Be(1);
    }

    [Test]
    public async Task HandleRpc_QueueMoveTop_BatchIds_PreservesRelativeOrder_ByIteratingInReverse()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[10, 20, 30]");
        args["ids"] = idsDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "queue-move-top",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "top");
            await this.torrentService.MoveQueueAsync(20, "top");
            await this.torrentService.MoveQueueAsync(10, "top");
        });
    }

    [Test]
    public async Task HandleRpc_QueueMoveBottom_BatchIds_PreservesRelativeOrder_ByIteratingInForward()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[10, 20, 30]");
        args["ids"] = idsDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "queue-move-bottom",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(10, "bottom");
            await this.torrentService.MoveQueueAsync(20, "bottom");
            await this.torrentService.MoveQueueAsync(30, "bottom");
        });
    }

    [Test]
    public async Task HandleRpc_QueueMoveUp_BatchIds_ProcessesInAscendingQueuePositionOrder()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrentA = new Torrent { Id = 10, QueuePosition = 4 };
        var torrentB = new Torrent { Id = 20, QueuePosition = 2 };
        var torrentC = new Torrent { Id = 30, QueuePosition = 3 };
        this.torrentService.Get(10).Returns(torrentA);
        this.torrentService.Get(20).Returns(torrentB);
        this.torrentService.Get(30).Returns(torrentC);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[10, 20, 30]");
        args["ids"] = idsDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "queue-move-up",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(20, "up");
            await this.torrentService.MoveQueueAsync(30, "up");
            await this.torrentService.MoveQueueAsync(10, "up");
        });
    }

    [Test]
    public async Task HandleRpc_QueueMoveDown_BatchIds_ProcessesInDescendingQueuePositionOrder()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrentA = new Torrent { Id = 10, QueuePosition = 2 };
        var torrentB = new Torrent { Id = 20, QueuePosition = 3 };
        var torrentC = new Torrent { Id = 30, QueuePosition = 4 };
        this.torrentService.Get(10).Returns(torrentA);
        this.torrentService.Get(20).Returns(torrentB);
        this.torrentService.Get(30).Returns(torrentC);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[10, 20, 30]");
        args["ids"] = idsDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "queue-move-down",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "down");
            await this.torrentService.MoveQueueAsync(20, "down");
            await this.torrentService.MoveQueueAsync(10, "down");
        });
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("\"true\"", true)]
    [TestCase("\"false\"", false)]
    [TestCase("\"1\"", true)]
    [TestCase("\"0\"", false)]
    [TestCase("null", false)]
    public async Task HandleRpc_TorrentAdd_WithVariousBooleanFormatsForPaused_DoesNotThrowAndParsesCorrectly(string pausedJson, bool expectedPaused)
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var parsed = new ParsedTorrent
        {
            Name = "Test.Torrent",
            InfoHash = "1234567890123456789012345678901234567890",
            TotalSize = 1024,
        };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.GetByInfoHash(parsed.InfoHash).Returns((Torrent)null);
        var added = new Torrent
        {
            Id = 1,
            Name = parsed.Name,
            InfoHash = parsed.InfoHash,
            Status = expectedPaused ? TorrentStatus.Paused : TorrentStatus.Downloading,
        };
        this.torrentService.AddFromParsedTorrentAsync(parsed, Arg.Any<string>(), Arg.Any<string>(), expectedPaused, Arg.Any<byte[]>()).Returns(added);

        var args = new Dictionary<string, JsonElement>();
        var fakeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("d8:announce3:url4:infod6:lengthi1024eee"));
        using var metaDoc = JsonDocument.Parse($"\"{fakeB64}\"");
        using var pausedDoc = JsonDocument.Parse(pausedJson);
        args["metainfo"] = metaDoc.RootElement.Clone();
        args["paused"] = pausedDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-add",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, Arg.Any<string>(), Arg.Any<string>(), expectedPaused, Arg.Any<byte[]>());
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("\"true\"", true)]
    [TestCase("\"false\"", false)]
    [TestCase("\"1\"", true)]
    [TestCase("\"0\"", false)]
    [TestCase("null", false)]
    public async Task HandleRpc_TorrentRemove_WithVariousBooleanFormatsForDeleteLocalData_DoesNotThrowAndParsesCorrectly(string deleteDataJson, bool expectedDelete)
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[1]");
        using var delDoc = JsonDocument.Parse(deleteDataJson);
        args["ids"] = idsDoc.RootElement.Clone();
        args["delete-local-data"] = delDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-remove",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        await this.torrentService.Received(1).DeleteAsync(1, expectedDelete);
    }

    [Test]
    public async Task HandleRpc_BlocklistUpdate_InvokesBlocklistUpdate_AndReturnsBlocklistSize()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.blocklistUpdateService.UpdateRulesAsync(Arg.Any<System.Threading.CancellationToken>()).Returns(Task.FromResult(4242));

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "blocklist-update",
            Tag = JsonDocument.Parse("123").RootElement,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");
        response.Tag.Should().Be(123L);

        var args = response.Arguments as Dictionary<string, object>;
        args.Should().NotBeNull();
        args!["blocklist-size"].Should().Be(4242);

        await this.blocklistUpdateService.Received(1).UpdateRulesAsync(Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task HandleRpc_SessionClose_ReturnsSuccessResponse()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-close",
            Tag = JsonDocument.Parse("77").RootElement,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");
        response.Tag.Should().Be(77L);
    }

    [Test]
    public async Task HandleRpc_TagNullPreservation_WhenNoTagProvided_ReturnsNullTag()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "session-get",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");
        response.Tag.Should().BeNull();
    }

    [Test]
    public async Task HandleRpc_TorrentGet_PopulatesMissingTransmission3And4Fields()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testTorrent = new Torrent
        {
            Id = 1,
            Name = "Ubuntu Linux 24.04 ISO",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1000000L,
            Priority = 1,
            DateAdded = DateTime.UtcNow.AddHours(-2),
            LastActive = DateTime.UtcNow.AddMinutes(-5),
            TrackerUrl = "http://tracker.ubuntu.com/announce",
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { testTorrent });

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.IsStalled.Returns(true);
        this.downloadEngine.GetTask(1).Returns(mockTask);

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as TransmissionRpcResponse;
        response.Should().NotBeNull();
        response!.Result.Should().Be("success");

        var args = response.Arguments as Dictionary<string, object>;
        args.Should().NotBeNull();
        var torrents = args!["torrents"] as List<Dictionary<string, object>>;
        torrents.Should().NotBeNull();
        torrents.Should().HaveCount(1);

        var t = torrents![0];
        t["bandwidthPriority"].Should().Be(1);
        t["corruptEver"].Should().Be(0L);
        t["desiredAvailable"].Should().Be(500000L);
        ((long)t["editDate"]).Should().BeGreaterThan(0L);
        t["etaIdle"].Should().Be(-1L);
        t["group"].Should().Be(string.Empty);
        t["isStalled"].Should().Be(true);
        t["magnetLink"].ToString().Should().StartWith("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567");
        t["magnetLink"].ToString().Should().Contain("dn=Ubuntu%20Linux%2024.04%20ISO");
        t["manualAnnounceTime"].Should().Be(0L);
        ((int)t["maxConnectedPeers"]).Should().BeGreaterThan(0);
        t["metadataPercentComplete"].Should().Be(1.0);
        t["percentComplete"].Should().Be(0.5);
        ((long)t["startDate"]).Should().BeGreaterThan(0L);
        t["torrentFile"].Should().Be(string.Empty);
        t["trackerList"].Should().Be("http://tracker.ubuntu.com/announce");
        t["wanted"].Should().NotBeNull();
        t["webseeds"].Should().NotBeNull();
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithBandwidthPriority_UpdatesPriority()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testTorrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
            Priority = 0,
        };
        this.torrentService.Get(1).Returns(testTorrent);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[1]");
        using var bpDoc = JsonDocument.Parse("1");
        args["ids"] = idsDoc.RootElement.Clone();
        args["bandwidthPriority"] = bpDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        testTorrent.Priority.Should().Be(1);
        await this.torrentService.Received(1).UpdateAsync(testTorrent);
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithTrackerAdd_AddsTrackers()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testTorrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
        };
        this.torrentService.Get(1).Returns(testTorrent);
        this.trackerEntryRepository.GetByTorrentId(1).Returns(new List<TrackerEntry>());

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[1]");
        using var trackerAddDoc = JsonDocument.Parse("[\"http://tracker1.org/announce\", \"http://tracker2.org/announce\"]");
        args["ids"] = idsDoc.RootElement.Clone();
        args["trackerAdd"] = trackerAddDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        this.trackerEntryRepository.Received(2).Insert(Arg.Any<TrackerEntry>());
        await this.downloadEngine.Received(1).AddTrackersAsync(1, Arg.Is<IEnumerable<string>>(urls => urls.Count() == 2));
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithTrackerRemove_RemovesTrackers()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testTorrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
        };
        this.torrentService.Get(1).Returns(testTorrent);

        var existingTracker = new TrackerEntry { Id = 10, TorrentId = 1, Url = "http://removetracker.org/announce" };
        this.trackerEntryRepository.Get(10).Returns(existingTracker);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[1]");
        using var trackerRemoveDoc = JsonDocument.Parse("[10]");
        args["ids"] = idsDoc.RootElement.Clone();
        args["trackerRemove"] = trackerRemoveDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        this.trackerEntryRepository.Received(1).Delete(10);
        await this.downloadEngine.Received(1).RemoveTrackersAsync(1, Arg.Is<IEnumerable<string>>(urls => urls.Contains("http://removetracker.org/announce")));
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithTrackerReplace_ReplacesTrackers()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testTorrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
        };
        this.torrentService.Get(1).Returns(testTorrent);

        var existingTracker = new TrackerEntry { Id = 10, TorrentId = 1, Url = "http://oldtracker.org/announce" };
        this.trackerEntryRepository.Get(10).Returns(existingTracker);

        var args = new Dictionary<string, JsonElement>();
        using var idsDoc = JsonDocument.Parse("[1]");
        using var trackerReplaceDoc = JsonDocument.Parse("[[10, \"http://newtracker.org/announce\"]]");
        args["ids"] = idsDoc.RootElement.Clone();
        args["trackerReplace"] = trackerReplaceDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        existingTracker.Url.Should().Be("http://newtracker.org/announce");
        this.trackerEntryRepository.Received(1).Update(existingTracker);
        await this.downloadEngine.Received(1).RemoveTrackersAsync(1, Arg.Is<IEnumerable<string>>(urls => urls.Contains("http://oldtracker.org/announce")));
        await this.downloadEngine.Received(1).AddTrackersAsync(1, Arg.Is<IEnumerable<string>>(urls => urls.Contains("http://newtracker.org/announce")));
    }

    [Test]
    public async Task HandleRpc_FreeSpace_ReturnsAvailableSpaceFromDiskProvider()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.diskProvider.GetAvailableSpace("/downloads").Returns(107374182400L);
        this.diskProvider.GetTotalSize("/downloads").Returns(536870912000L);

        var args = new Dictionary<string, JsonElement>();
        using var pathDoc = JsonDocument.Parse("\"/downloads\"");
        args["path"] = pathDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "free-space",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var resArgs = doc.RootElement.GetProperty("arguments");
        resArgs.GetProperty("path").GetString().Should().Be("/downloads");
        resArgs.GetProperty("size-bytes").GetInt64().Should().Be(107374182400L);
        resArgs.GetProperty("total_size").GetInt64().Should().Be(536870912000L);
    }

    [Test]
    public async Task HandleRpc_TorrentGet_MapsPeerPropertiesCorrectly()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testTorrent = new Torrent
        {
            Id = 5,
            Name = "Peers Torrent",
            InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { testTorrent });

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.GetPeers().Returns(new List<PeerInfo>
        {
            new PeerInfo
            {
                Ip = "192.168.1.50",
                Port = 6881,
                Client = "qBittorrent/4.5.0",
                DownloadSpeed = 102400,
                UploadSpeed = 51200,
                Progress = 0.75,
                Flags = "uE",
                IsEncrypted = true,
                IsUtp = true,
                IsIncoming = false,
                IsChoked = false,
                IsInterested = true,
                ClientIsChoked = true,
                ClientIsInterested = false,
            },
        });
        this.downloadEngine.GetTask(5).Returns(mockTask);

        var args = new Dictionary<string, JsonElement>();
        using var fieldsDoc = JsonDocument.Parse("[\"id\", \"peers\"]");
        args["fields"] = fieldsDoc.RootElement.Clone();

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = args,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var torrents = doc.RootElement.GetProperty("arguments").GetProperty("torrents");
        torrents.GetArrayLength().Should().Be(1);
        var peerElem = torrents[0].GetProperty("peers")[0];

        peerElem.GetProperty("address").GetString().Should().Be("192.168.1.50");
        peerElem.GetProperty("isEncrypted").GetBoolean().Should().BeTrue();
        peerElem.GetProperty("isUTP").GetBoolean().Should().BeTrue();
        peerElem.GetProperty("isIncoming").GetBoolean().Should().BeFalse();
        peerElem.GetProperty("peerIsChoked").GetBoolean().Should().BeFalse();
        peerElem.GetProperty("peerIsInterested").GetBoolean().Should().BeTrue();
        peerElem.GetProperty("clientIsChoked").GetBoolean().Should().BeTrue();
        peerElem.GetProperty("clientIsInterested").GetBoolean().Should().BeFalse();
    }
}
