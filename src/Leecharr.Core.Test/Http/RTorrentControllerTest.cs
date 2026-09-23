// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.RTorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class RTorrentControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private ITorrentFileService torrentFileService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private RTorrentController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new RTorrentController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);
    }

    private void SetRequestBody(string xml)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(xml);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    [Test]
    public async Task HandleXmlRpc_DirectorySet_WithInfoHash_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.directory.set</methodName>
                <params>
                <param><value><string>aabbccddeeff00112233445566778899aabbccdd</string></value></param>
                <param><value><string>/downloads/new_dir</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/new_dir", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_DirectoryBaseSet_WithInfoHash_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.directory_base.set</methodName>
                <params>
                <param><value><string>aabbccddeeff00112233445566778899aabbccdd</string></value></param>
                <param><value><string>/downloads/base_dir</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/base_dir", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_DirectorySet_WithTorrentId_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.Get(42).Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.directory.set</methodName>
                <params>
                <param><value><i4>42</i4></value></param>
                <param><value><string>/downloads/id_target</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/id_target", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_SystemMulticall_WithBatchedQueries_ReturnsArrayOfResults()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent.One",
            InfoHash = "1111111111111111111111111111111111111111",
            Category = "tv",
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Name = "Torrent.Two",
            InfoHash = "2222222222222222222222222222222222222222",
            Category = "movies",
        };

        this.torrentService.GetByInfoHash("1111111111111111111111111111111111111111").Returns(torrent1);
        this.torrentService.GetByInfoHash("2222222222222222222222222222222222222222").Returns(torrent2);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>system.multicall</methodName>
                <params>
                <param>
                    <value>
                    <array>
                        <data>
                        <value>
                            <struct>
                            <member>
                                <name>methodName</name>
                                <value><string>d.name</string></value>
                            </member>
                            <member>
                                <name>params</name>
                                <value>
                                <array>
                                    <data>
                                    <value><string>1111111111111111111111111111111111111111</string></value>
                                    </data>
                                </array>
                                </value>
                            </member>
                            </struct>
                        </value>
                        <value>
                            <struct>
                            <member>
                                <name>methodName</name>
                                <value><string>d.get_custom1</string></value>
                            </member>
                            <member>
                                <name>params</name>
                                <value>
                                <array>
                                    <data>
                                    <value><string>2222222222222222222222222222222222222222</string></value>
                                    </data>
                                </array>
                                </value>
                            </member>
                            </struct>
                        </value>
                        </data>
                    </array>
                    </value>
                </param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string>Torrent.One</string>");
        contentResult.Content.Should().Contain("<string>movies</string>");
    }

    [Test]
    public async Task HandleXmlRpc_SystemMulticall_WithGetDirectoryAndVersion_ReturnsExpectedResults()
    {
        this.configService.DownloadDir.Returns("/data/downloads");

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>system.multicall</methodName>
                <params>
                <param>
                    <value>
                    <array>
                        <data>
                        <value>
                            <struct>
                            <member>
                                <name>methodName</name>
                                <value><string>get_directory</string></value>
                            </member>
                            <member>
                                <name>params</name>
                                <value>
                                <array>
                                    <data />
                                </array>
                                </value>
                            </member>
                            </struct>
                        </value>
                        <value>
                            <struct>
                            <member>
                                <name>methodName</name>
                                <value><string>system.client_version</string></value>
                            </member>
                            <member>
                                <name>params</name>
                                <value>
                                <array>
                                    <data />
                                </array>
                                </value>
                            </member>
                            </struct>
                        </value>
                        </data>
                    </array>
                    </value>
                </param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string>/data/downloads</string>");
        contentResult.Content.Should().Contain("<string>0.9.8</string>");
    }

    [Test]
    public async Task HandleXmlRpc_FMulticall_ReturnsChunkMetricsAndRangeFields()
    {
        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "4444444444444444444444444444444444444444",
        };
        var files = new List<TorrentFile>
        {
            new TorrentFile
            {
                Id = 1,
                TorrentId = 10,
                Path = "test.mkv",
                Size = 10485760,
                PieceOffset = 10,
                PieceCount = 20,
                Progress = 0.5,
                Priority = 4,
            },
        };

        this.torrentService.GetByInfoHash("4444444444444444444444444444444444444444").Returns(torrent);
        this.torrentFileService.GetFiles(10).Returns(files);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>f.multicall</methodName>
                <params>
                <param><value><string>4444444444444444444444444444444444444444</string></value></param>
                <param><value><string></string></value></param>
                <param><value><string>f.get_path=</string></value></param>
                <param><value><string>f.get_size_bytes=</string></value></param>
                <param><value><string>f.get_completed_chunks=</string></value></param>
                <param><value><string>f.get_size_chunks=</string></value></param>
                <param><value><string>f.get_range_first=</string></value></param>
                <param><value><string>f.get_range_second=</string></value></param>
                <param><value><string>f.get_priority=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string>test.mkv</string>");
        contentResult.Content.Should().Contain("<i8>10485760</i8>");
        contentResult.Content.Should().Contain("<i8>10</i8>");
        contentResult.Content.Should().Contain("<i8>20</i8>");
        contentResult.Content.Should().Contain("<i8>10</i8>");
        contentResult.Content.Should().Contain("<i8>29</i8>");
        contentResult.Content.Should().Contain("<i4>2</i4>");
    }

    [Test]
    public async Task HandleXmlRpc_TrackerAnnounce_WithInfoHash_InvokesForceAnnounceAsync()
    {
        var torrent = new Torrent
        {
            Id = 55,
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
        };
        this.torrentService.GetByInfoHash("1234567890abcdef1234567890abcdef12345678").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.tracker_announce</methodName>
                <params>
                <param><value><string>1234567890abcdef1234567890abcdef12345678</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).ForceAnnounceAsync(55);
    }

    [Test]
    public async Task HandleXmlRpc_TrackerAnnounceDotted_WithInfoHash_InvokesForceAnnounceAsync()
    {
        var torrent = new Torrent
        {
            Id = 56,
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
        };
        this.torrentService.GetByInfoHash("abcdefabcdefabcdefabcdefabcdefabcdefabcd").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.tracker.announce</methodName>
                <params>
                <param><value><string>abcdefabcdefabcdefabcdefabcdefabcdefabcd</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).ForceAnnounceAsync(56);
    }

    [Test]
    public async Task HandleXmlRpc_TorrentDownRateSet_WithBytes_UpdatesTorrentDownloadLimit()
    {
        var torrent = new Torrent
        {
            Id = 77,
            InfoHash = "feedbeefcafefeedbeefcafefeedbeefcafefeed",
            DownloadLimit = 0,
        };
        this.torrentService.GetByInfoHash("feedbeefcafefeedbeefcafefeedbeefcafefeed").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.down.rate.set</methodName>
                <params>
                <param><value><string>feedbeefcafefeedbeefcafefeedbeefcafefeed</string></value></param>
                <param><value><i8>5242880</i8></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        torrent.DownloadLimit.Should().Be(5120);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_TorrentDownRateSetKb_WithKb_UpdatesTorrentDownloadLimit()
    {
        var torrent = new Torrent
        {
            Id = 78,
            InfoHash = "feedbeefcafefeedbeefcafefeedbeefcafefeed",
            DownloadLimit = 0,
        };
        this.torrentService.GetByInfoHash("feedbeefcafefeedbeefcafefeedbeefcafefeed").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.down.rate.set_kb</methodName>
                <params>
                <param><value><string>feedbeefcafefeedbeefcafefeedbeefcafefeed</string></value></param>
                <param><value><i4>3500</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        torrent.DownloadLimit.Should().Be(3500);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_TorrentUpRateSet_WithBytes_UpdatesTorrentUploadLimit()
    {
        var torrent = new Torrent
        {
            Id = 79,
            InfoHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            UploadLimit = 0,
        };
        this.torrentService.GetByInfoHash("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.up.rate.set</methodName>
                <params>
                <param><value><string>deadbeefdeadbeefdeadbeefdeadbeefdeadbeef</string></value></param>
                <param><value><i8>2097152</i8></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        torrent.UploadLimit.Should().Be(2048);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_TorrentUpRateSetKb_WithKb_UpdatesTorrentUploadLimit()
    {
        var torrent = new Torrent
        {
            Id = 80,
            InfoHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            UploadLimit = 0,
        };
        this.torrentService.GetByInfoHash("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.up.rate.set_kb</methodName>
                <params>
                <param><value><string>deadbeefdeadbeefdeadbeefdeadbeefdeadbeef</string></value></param>
                <param><value><i4>1500</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        torrent.UploadLimit.Should().Be(1500);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_ThrottleGlobalDownMaxRateSetKb_SavesConfig()
    {
        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>throttle.global_down.max_rate.set_kb</methodName>
                <params>
                <param><value><i4>12000</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (int)d["MaxDownloadSpeedKbps"] == 12000));
    }

    [Test]
    public async Task HandleXmlRpc_ThrottleGlobalUpMaxRateSetKb_SavesConfig()
    {
        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>throttle.global_up.max_rate.set_kb</methodName>
                <params>
                <param><value><i4>6000</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (int)d["MaxUploadSpeedKbps"] == 6000));
    }

    [Test]
    public async Task HandleXmlRpc_SetDownloadRate_WithBytes_SavesConfigInKb()
    {
        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>set_download_rate</methodName>
                <params>
                <param><value><i8>10485760</i8></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (int)d["MaxDownloadSpeedKbps"] == 10240));
    }

    [Test]
    public async Task HandleXmlRpc_SetUploadRate_WithBytes_SavesConfigInKb()
    {
        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>set_upload_rate</methodName>
                <params>
                <param><value><i8>5242880</i8></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (int)d["MaxUploadSpeedKbps"] == 5120));
    }

    [Test]
    public async Task HandleXmlRpc_SetCustom1_UpdatesTorrentCategory()
    {
        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "hash10",
            Category = "initial",
        };
        this.torrentService.GetByInfoHash("hash10").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.set_custom1</methodName>
                <params>
                <param><value><string>hash10</string></value></param>
                <param><value><string>tv-sonarr</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string>tv-sonarr</string>");
        torrent.Category.Should().Be("tv-sonarr");
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_SetDirectory_UpdatesTorrentLocation()
    {
        var torrent = new Torrent
        {
            Id = 20,
            InfoHash = "hash20",
            SavePath = "/downloads/initial",
        };
        this.torrentService.GetByInfoHash("hash20").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.set_directory</methodName>
                <params>
                <param><value><string>hash20</string></value></param>
                <param><value><string>/downloads/tv</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(20, "/downloads/tv", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_SetDirectoryBase_UpdatesTorrentLocation()
    {
        var torrent = new Torrent
        {
            Id = 21,
            InfoHash = "hash21",
            SavePath = "/downloads/initial",
        };
        this.torrentService.GetByInfoHash("hash21").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.set_directory_base</methodName>
                <params>
                <param><value><string>hash21</string></value></param>
                <param><value><string>/downloads/movies</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(21, "/downloads/movies", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_SetPriority_UpdatesTorrentPriority()
    {
        var torrent = new Torrent
        {
            Id = 30,
            InfoHash = "hash30",
            Priority = 0,
        };
        this.torrentService.GetByInfoHash("hash30").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.set_priority</methodName>
                <params>
                <param><value><string>hash30</string></value></param>
                <param><value><i4>2</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        torrent.Priority.Should().Be(2);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_Custom2ThroughCustom5_GetReturnsEmptyStringWhenUnset_AndReturnsStoredValueWhenSet()
    {
        var torrent = new Torrent
        {
            Id = 40,
            InfoHash = "hash40",
            Category = "movies",
        };
        this.torrentService.GetByInfoHash("hash40").Returns(torrent);

        // 1. Initially unset custom2 returns empty string
        var getXml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.custom2</methodName>
                <params>
                <param><value><string>hash40</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(getXml);
        var getRes = (ContentResult)await this.controller.HandleXmlRpc();
        getRes.Content.Should().Contain("<string></string>");

        // 2. Set custom2 through custom5
        var setXml2 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.set_custom2</methodName>
                <params>
                <param><value><string>hash40</string></value></param>
                <param><value><string>val-custom2</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(setXml2);
        var setRes2 = (ContentResult)await this.controller.HandleXmlRpc();
        setRes2.Content.Should().Contain("<string>val-custom2</string>");

        var setXml3 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.custom3.set</methodName>
                <params>
                <param><value><string>hash40</string></value></param>
                <param><value><string>val-custom3</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(setXml3);
        var setRes3 = (ContentResult)await this.controller.HandleXmlRpc();
        setRes3.Content.Should().Contain("<string>val-custom3</string>");

        var setXml4 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.set_custom4</methodName>
                <params>
                <param><value><string>hash40</string></value></param>
                <param><value><string>val-custom4</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(setXml4);
        var setRes4 = (ContentResult)await this.controller.HandleXmlRpc();
        setRes4.Content.Should().Contain("<string>val-custom4</string>");

        var setXml5 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.custom5.set</methodName>
                <params>
                <param><value><string>hash40</string></value></param>
                <param><value><string>val-custom5</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(setXml5);
        var setRes5 = (ContentResult)await this.controller.HandleXmlRpc();
        setRes5.Content.Should().Contain("<string>val-custom5</string>");

        // 3. Get custom2 through custom5
        var verifyXml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_custom2</methodName>
                <params>
                <param><value><string>hash40</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(verifyXml);
        var verifyRes = (ContentResult)await this.controller.HandleXmlRpc();
        verifyRes.Content.Should().Contain("<string>val-custom2</string>");
    }

    [Test]
    public async Task HandleXmlRpc_Multicall_FiltersTorrentsByView()
    {
        var tStarted = new Torrent { Id = 1, InfoHash = "1111", Status = TorrentStatus.Downloading, Progress = 0.5 };
        var tSeeding = new Torrent { Id = 2, InfoHash = "2222", Status = TorrentStatus.Seeding, Progress = 1.0 };
        var tStopped = new Torrent { Id = 3, InfoHash = "3333", Status = TorrentStatus.Stopped, Progress = 0.2 };
        var tPaused = new Torrent { Id = 4, InfoHash = "4444", Status = TorrentStatus.Paused, Progress = 1.0 };

        this.torrentService.GetAll().Returns(new List<Torrent> { tStarted, tSeeding, tStopped, tPaused });

        // Started view (downloading or seeding)
        var xmlStarted = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>started</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlStarted);
        var resStarted = (ContentResult)await this.controller.HandleXmlRpc();
        resStarted.Content.Should().Contain("1111");
        resStarted.Content.Should().Contain("2222");
        resStarted.Content.Should().NotContain("3333");
        resStarted.Content.Should().NotContain("4444");

        // Stopped view (stopped or paused)
        var xmlStopped = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall</methodName>
                <params>
                <param><value><string>stopped</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlStopped);
        var resStopped = (ContentResult)await this.controller.HandleXmlRpc();
        resStopped.Content.Should().Contain("3333");
        resStopped.Content.Should().Contain("4444");
        resStopped.Content.Should().NotContain("1111");
        resStopped.Content.Should().NotContain("2222");

        // Complete view
        var xmlComplete = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall</methodName>
                <params>
                <param><value><string>complete</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlComplete);
        var resComplete = (ContentResult)await this.controller.HandleXmlRpc();
        resComplete.Content.Should().Contain("2222");
        resComplete.Content.Should().Contain("4444");
        resComplete.Content.Should().NotContain("1111");
        resComplete.Content.Should().NotContain("3333");

        // Incomplete view
        var xmlIncomplete = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall</methodName>
                <params>
                <param><value><string>incomplete</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlIncomplete);
        var resIncomplete = (ContentResult)await this.controller.HandleXmlRpc();
        resIncomplete.Content.Should().Contain("1111");
        resIncomplete.Content.Should().Contain("3333");
        resIncomplete.Content.Should().NotContain("2222");
        resIncomplete.Content.Should().NotContain("4444");
    }

    [Test]
    public async Task HandleXmlRpc_Multicall2_FiltersTorrentsByPausedAndCheckingViews()
    {
        var tChecking = new Torrent { Id = 1, InfoHash = "1111", Status = TorrentStatus.Checking, Progress = 0.5 };
        var tPaused = new Torrent { Id = 2, InfoHash = "2222", Status = TorrentStatus.Paused, Progress = 1.0 };
        var tDownloading = new Torrent { Id = 3, InfoHash = "3333", Status = TorrentStatus.Downloading, Progress = 0.2 };

        this.torrentService.GetAll().Returns(new List<Torrent> { tChecking, tPaused, tDownloading });

        // Checking view
        var xmlChecking = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>checking</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlChecking);
        var resChecking = (ContentResult)await this.controller.HandleXmlRpc();
        resChecking.Content.Should().Contain("1111");
        resChecking.Content.Should().NotContain("2222");
        resChecking.Content.Should().NotContain("3333");

        // Paused view
        var xmlPaused = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>paused</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlPaused);
        var resPaused = (ContentResult)await this.controller.HandleXmlRpc();
        resPaused.Content.Should().Contain("2222");
        resPaused.Content.Should().NotContain("1111");
        resPaused.Content.Should().NotContain("3333");

        // Unknown view returns all torrents
        var xmlUnknown = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>unknown_view</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlUnknown);
        var resUnknown = (ContentResult)await this.controller.HandleXmlRpc();
        resUnknown.Content.Should().Contain("1111");
        resUnknown.Content.Should().Contain("2222");
        resUnknown.Content.Should().Contain("3333");
    }

    [Test]
    public async Task HandleXmlRpc_Multicall2_IncompleteView_ExcludesSeedingTorrentsEvenIfProgressUnderOne()
    {
        var tSeedingUnderOne = new Torrent { Id = 1, InfoHash = "1111", Status = TorrentStatus.Seeding, Progress = 0.999 };
        var tDownloading = new Torrent { Id = 2, InfoHash = "2222", Status = TorrentStatus.Downloading, Progress = 0.5 };

        this.torrentService.GetAll().Returns(new List<Torrent> { tSeedingUnderOne, tDownloading });

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>incomplete</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);
        var res = (ContentResult)await this.controller.HandleXmlRpc();
        res.Content.Should().Contain("2222");
        res.Content.Should().NotContain("1111");
    }

    [Test]
    public async Task HandleXmlRpc_BasePathAndBaseFilename_ReturnsPayloadPathAndName()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "aaaa1111bbbb2222cccc3333dddd4444eeee5555",
            Name = "My.Cool.Show.S01E01.mkv",
            SavePath = "/downloads/completed",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
        };

        this.torrentService.GetByInfoHash("aaaa1111bbbb2222cccc3333dddd4444eeee5555").Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>main</string></value></param>
                <param><value><string>d.base_path=</string></value></param>
                <param><value><string>d.get_base_path=</string></value></param>
                <param><value><string>d.base_filename=</string></value></param>
                <param><value><string>d.get_base_filename=</string></value></param>
                <param><value><string>d.directory=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);
        var res = (ContentResult)await this.controller.HandleXmlRpc();

        var combinedPath = Path.Combine("/downloads/completed", "My.Cool.Show.S01E01.mkv");
        res.Content.Should().Contain($"<string>{combinedPath}</string>");
        res.Content.Should().Contain("<string>My.Cool.Show.S01E01.mkv</string>");
        res.Content.Should().Contain("<string>/downloads/completed</string>");
    }

    [Test]
    public async Task HandleXmlRpc_ViewsHas_ChecksMembershipStandaloneAndInMulticall()
    {
        var torrent = new Torrent
        {
            Id = 50,
            InfoHash = "hash50",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetByInfoHash("hash50").Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        // Standalone started
        var xml1 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.views.has</methodName>
                <params>
                <param><value><string>hash50</string></value></param>
                <param><value><string>started</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml1);
        var res1 = (ContentResult)await this.controller.HandleXmlRpc();
        res1.Content.Should().Contain("<i4>1</i4>");

        // Standalone complete
        var xml2 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.views.has</methodName>
                <params>
                <param><value><string>hash50</string></value></param>
                <param><value><string>complete</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml2);
        var res2 = (ContentResult)await this.controller.HandleXmlRpc();
        res2.Content.Should().Contain("<i4>0</i4>");

        // Multicall with d.views.has=started
        var xml3 = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall</methodName>
                <params>
                <param><value><string>main</string></value></param>
                <param><value><string>d.views.has=started</string></value></param>
                <param><value><string>d.views.has=complete</string></value></param>
                <param><value><string>d.custom2</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml3);
        var res3 = (ContentResult)await this.controller.HandleXmlRpc();
        res3.Content.Should().Contain("<i4>1</i4>");
        res3.Content.Should().Contain("<i4>0</i4>");
        res3.Content.Should().Contain("<string></string>");
    }

    [Test]
    public async Task HandleXmlRpc_CompletedAndLeftBytes_AlignsWithProgress()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Progress.Torrent",
            InfoHash = "1111111111111111111111111111111111111111",
            TotalSize = 10_000_000L,
            Downloaded = 50_000_000L, // Raw transfer exceeds size due to retransmission/waste
            Progress = 0.75,
            Status = TorrentStatus.Downloading,
        };
        this.torrentService.GetByInfoHash("1111111111111111111111111111111111111111").Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall</methodName>
                <params>
                <param><value><string>main</string></value></param>
                <param><value><string>d.completed_bytes=</string></value></param>
                <param><value><string>d.get_completed_bytes=</string></value></param>
                <param><value><string>d.bytes_done=</string></value></param>
                <param><value><string>d.get_bytes_done=</string></value></param>
                <param><value><string>d.left_bytes=</string></value></param>
                <param><value><string>d.get_left_bytes=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var res = (ContentResult)await this.controller.HandleXmlRpc();

        // 0.75 * 10,000,000 = 7,500,000 completed
        res.Content.Should().Contain("<i8>7500000</i8>");
        res.Content.Should().NotContain("<i8>50000000</i8>");
        // 0.25 * 10,000,000 = 2,500,000 left
        res.Content.Should().Contain("<i8>2500000</i8>");

        // Test fully complete torrent with 0 raw downloaded (e.g. seeded/imported)
        var completeTorrent = new Torrent
        {
            Id = 2,
            Name = "Complete.Torrent",
            InfoHash = "2222222222222222222222222222222222222222",
            TotalSize = 10_000_000L,
            Downloaded = 0L,
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
        };
        this.torrentService.GetByInfoHash("2222222222222222222222222222222222222222").Returns(completeTorrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { completeTorrent });

        var xmlComplete = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall</methodName>
                <params>
                <param><value><string>2222222222222222222222222222222222222222</string></value></param>
                <param><value><string>d.completed_bytes=</string></value></param>
                <param><value><string>d.left_bytes=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xmlComplete);

        var resComplete = (ContentResult)await this.controller.HandleXmlRpc();
        resComplete.Content.Should().Contain("<i8>10000000</i8>");
        resComplete.Content.Should().Contain("<i8>0</i8>");
    }

    [Test]
    public async Task HandleXmlRpc_SystemMulticall_WhenSubcallFails_ReturnsFaultStructWithoutFailingEntireMulticall()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Success.Torrent",
            InfoHash = "1111111111111111111111111111111111111111",
        };
        this.torrentService.GetByInfoHash("1111111111111111111111111111111111111111").Returns(torrent);

        this.torrentService.GetByInfoHash("9999999999999999999999999999999999999999")
            .Returns(_ => throw new System.InvalidOperationException("Failed to query torrent"));

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>system.multicall</methodName>
                <params>
                <param>
                    <value>
                    <array>
                        <data>
                        <value>
                            <struct>
                            <member>
                                <name>methodName</name>
                                <value><string>d.name</string></value>
                            </member>
                            <member>
                                <name>params</name>
                                <value>
                                <array>
                                    <data>
                                    <value><string>1111111111111111111111111111111111111111</string></value>
                                    </data>
                                </array>
                                </value>
                            </member>
                            </struct>
                        </value>
                        <value>
                            <struct>
                            <member>
                                <name>methodName</name>
                                <value><string>d.erase</string></value>
                            </member>
                            <member>
                                <name>params</name>
                                <value>
                                <array>
                                    <data>
                                    <value><string>9999999999999999999999999999999999999999</string></value>
                                    </data>
                                </array>
                                </value>
                            </member>
                            </struct>
                        </value>
                        </data>
                    </array>
                    </value>
                </param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;

        // Multicall overall returned array with 2 responses
        contentResult.Content.Should().Contain("<methodResponse><params><param><value><array><data>");
        // First subcall succeeded
        contentResult.Content.Should().Contain("<string>Success.Torrent</string>");
        // Second subcall returned a fault struct
        contentResult.Content.Should().Contain("<name>faultCode</name><value><int>1</int></value>");
        contentResult.Content.Should().Contain("<name>faultString</name><value><string>Failed to query torrent</string></value>");
    }

    [Test]
    public async Task HandleXmlRpc_DMulticall2_WithRequestedFields_ReturnsExpectedData()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "MultiCall.Movie.2024",
            InfoHash = "11223344556677889900aabbccddeeff00112233",
            TotalSize = 1000000000L,
            PieceLength = 1000000,
            PieceCount = 1000,
            Progress = 0.5,
            DownloadSpeed = 2500000,
            UploadSpeed = 500000,
            Status = TorrentStatus.Downloading,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.multicall2</methodName>
                <params>
                <param><value><string></string></value></param>
                <param><value><string>main</string></value></param>
                <param><value><string>d.get_name=</string></value></param>
                <param><value><string>d.get_down_rate=</string></value></param>
                <param><value><string>d.get_up_rate=</string></value></param>
                <param><value><string>d.get_size_bytes=</string></value></param>
                <param><value><string>d.get_completed_chunks=</string></value></param>
                <param><value><string>d.hash=</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;

        contentResult.Content.Should().Contain("<string>MultiCall.Movie.2024</string>");
        contentResult.Content.Should().Contain("<i8>2500000</i8>");
        contentResult.Content.Should().Contain("<i8>500000</i8>");
        contentResult.Content.Should().Contain("<i8>1000000000</i8>");
        contentResult.Content.Should().Contain("<i8>500</i8>");
        contentResult.Content.Should().Contain("<string>11223344556677889900AABBCCDDEEFF00112233</string>");
    }

    [Test]
    public async Task HandleXmlRpc_DGetName_DirectCall_ReturnsTorrentName()
    {
        var hash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = hash,
            Name = "Direct.Call.Torrent",
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_name</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string>Direct.Call.Torrent</string>");
    }

    [Test]
    public async Task HandleXmlRpc_DGetName_DirectCall_WhenNotFound_ReturnsEmptyString()
    {
        var hash = "0000000000000000000000000000000000000000";
        this.torrentService.GetByInfoHash(hash).Returns((Torrent)null!);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_name</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string></string>");
    }

    [Test]
    public async Task HandleXmlRpc_DGetRates_DirectCalls_ReturnDownloadAndUploadRates()
    {
        var hash = "1234567890123456789012345678901234567890";
        var torrent = new Torrent
        {
            Id = 8,
            InfoHash = hash,
            DownloadSpeed = 123456,
            UploadSpeed = 65432,
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var downXml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_down_rate</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(downXml);
        var downResult = (ContentResult)await this.controller.HandleXmlRpc();
        downResult.Content.Should().Contain("<i8>123456</i8>");

        var upXml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_up_rate</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(upXml);
        var upResult = (ContentResult)await this.controller.HandleXmlRpc();
        upResult.Content.Should().Contain("<i8>65432</i8>");
    }

    [Test]
    public async Task HandleXmlRpc_DGetSizeBytes_DirectCall_ReturnsTotalSize()
    {
        var hash = "2222222222222222222222222222222222222222";
        var torrent = new Torrent
        {
            Id = 9,
            InfoHash = hash,
            TotalSize = 5368709120L,
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_size_bytes</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<i8>5368709120</i8>");
    }

    [Test]
    public async Task HandleXmlRpc_DGetCompletedChunks_DirectCall_ReturnsCalculatedChunks()
    {
        var hash = "3333333333333333333333333333333333333333";
        var torrent = new Torrent
        {
            Id = 11,
            InfoHash = hash,
            TotalSize = 2000000,
            PieceLength = 1000000,
            PieceCount = 2,
            Progress = 0.5,
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.get_completed_chunks</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<i8>1</i8>");
    }

    [Test]
    public async Task HandleXmlRpc_DHash_DirectCall_ReturnsUppercaseHash()
    {
        var hash = "4444444444444444444444444444444444444444";
        var torrent = new Torrent
        {
            Id = 12,
            InfoHash = hash,
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.hash</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<string>4444444444444444444444444444444444444444</string>");
    }

    [Test]
    public async Task HandleXmlRpc_DStart_DirectCall_InvokesResumeAsync()
    {
        var hash = "5555555555555555555555555555555555555555";
        var torrent = new Torrent { Id = 15, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.start</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).ResumeAsync(15);
    }

    [Test]
    public async Task HandleXmlRpc_DStop_DirectCall_InvokesPauseAsync()
    {
        var hash = "6666666666666666666666666666666666666666";
        var torrent = new Torrent { Id = 16, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.stop</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).PauseAsync(16);
    }

    [Test]
    public async Task HandleXmlRpc_DErase_WithDeleteFilesFalse_InvokesDeleteAsyncWithFalse()
    {
        var hash = "7777777777777777777777777777777777777777";
        var torrent = new Torrent { Id = 17, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.erase</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                <param><value><i4>0</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).DeleteAsync(17, false);
    }

    [Test]
    public async Task HandleXmlRpc_DErase_WithDeleteFilesTrue_InvokesDeleteAsyncWithTrue()
    {
        var hash = "8888888888888888888888888888888888888888";
        var torrent = new Torrent { Id = 18, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var xml = $"""
            <?xml version="1.0"?>
            <methodCall>
                <methodName>d.erase</methodName>
                <params>
                <param><value><string>{hash}</string></value></param>
                <param><value><i4>1</i4></value></param>
                </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).DeleteAsync(18, true);
    }

    [Test]
    public async Task HandleXmlRpc_GetDirectory_ReturnsDownloadDirectoryFromConfigService()
    {
        this.configService.DownloadDir.Returns("/custom/rtorrent/downloads");

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>get_directory</methodName>
                <params/>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = (ContentResult)await this.controller.HandleXmlRpc();
        result.Content.Should().Contain("<string>/custom/rtorrent/downloads</string>");
    }

    [Test]
    public async Task HandleXmlRpc_GlobalGetRates_ReturnsAggregatedRates()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, DownloadSpeed = 100000, UploadSpeed = 50000 },
            new() { Id = 2, DownloadSpeed = 200000, UploadSpeed = 70000 },
        };
        this.torrentService.GetAll().Returns(torrents);

        var dlXml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>get_down_rate</methodName>
                <params/>
            </methodCall>
            """;
        this.SetRequestBody(dlXml);
        var dlResult = (ContentResult)await this.controller.HandleXmlRpc();
        dlResult.Content.Should().Contain("<i8>300000</i8>");

        var ulXml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>get_up_rate</methodName>
                <params/>
            </methodCall>
            """;
        this.SetRequestBody(ulXml);
        var ulResult = (ContentResult)await this.controller.HandleXmlRpc();
        ulResult.Content.Should().Contain("<i8>120000</i8>");
    }

    [Test]
    public async Task HandleXmlRpc_InvalidXmlPayload_ReturnsFaultResponse()
    {
        this.SetRequestBody("<not-valid-xml-missing-closing-tag");

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<methodResponse><fault><value><struct>");
        contentResult.Content.Should().Contain("<name>faultCode</name><value><int>1</int></value>");
    }

    [Test]
    public async Task HandleXmlRpc_MissingOrUnhandledMethod_ReturnsZero()
    {
        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>completely.unknown.method</methodName>
                <params/>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
    }

    [Test]
    public async Task HandleXmlRpc_EmptyRequestBody_ReturnsDefaultVersion()
    {
        this.SetRequestBody(string.Empty);

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<string>0.9.8</string>");
    }

    [Test]
    public async Task HandleXmlRpc_WhenAuthenticationEnabledAndMissingCredentials_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key");

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
                <methodName>system.client_version</methodName>
                <params/>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();
        result.Should().BeOfType<UnauthorizedResult>();
        this.controller.Response.Headers.ContainsKey("WWW-Authenticate").Should().BeTrue();
    }
}
