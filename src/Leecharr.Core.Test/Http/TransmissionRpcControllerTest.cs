// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
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
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Torrents;

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

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");

        this.controller = new TransmissionRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.configService,
            diskSpaceService: this.diskSpaceService,
            configFileProvider: this.configFileProvider,
            diskProvider: this.diskProvider);
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
}
