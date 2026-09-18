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
}
