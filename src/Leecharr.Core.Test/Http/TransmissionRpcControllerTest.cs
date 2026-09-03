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
    private TransmissionRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");

        this.controller = new TransmissionRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.configService,
            configFileProvider: this.configFileProvider);
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
}
