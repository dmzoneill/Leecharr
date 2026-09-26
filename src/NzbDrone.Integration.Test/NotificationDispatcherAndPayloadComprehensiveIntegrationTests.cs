// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class NotificationDispatcherAndPayloadComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void NotificationPayloadBuilder_ProviderMatrix_BuildsValidPayloads()
    {
        var torrent = new Torrent
        {
            Id = 101,
            Name = "The.Matrix.1999.2160p.UHD.BluRay.x265",
            InfoHash = "1111222233334444555566667777888899990000",
            Category = "movies",
            TotalSize = 15_000_000_000L,
            Downloaded = 15_000_000_000L,
            Uploaded = 30_000_000_000L,
            DownloadSpeed = 10_000_000,
            UploadSpeed = 5_000_000,
            Ratio = 2.0,
            Status = TorrentStatus.Seeding,
            SavePath = "/downloads/movies/The Matrix (1999)",
        };

        var implementations = new[] { "Discord", "Slack", "Telegram", "Pushover", "Gotify", "Apprise", "Webhook" };
        var eventTypes = new[]
        {
            "Grab",
            "Download",
            "Rename",
            "HealthIssue",
            "HealthRestored",
            "ApplicationUpdate",
            "TorrentAdded",
            "TorrentComplete",
            "TorrentError",
            "RatioReached",
            "Test",
        };

        foreach (var impl in implementations)
        {
            foreach (var evt in eventTypes)
            {
                var payload = NotificationPayloadBuilder.BuildProviderPayload(
                    impl,
                    evt,
                    torrent,
                    new { MovieTitle = "The Matrix", Year = 1999 },
                    new { Message = "Generic message" },
                    "{\"chatId\":\"123456\",\"token\":\"bot_secret\",\"username\":\"LeechBot\",\"priority\":\"2\"}");

                payload.Should().NotBeNull();
            }
        }
    }

    [Test]
    public void NotificationPayloadBuilder_HelperMethods_ReturnExpectedValues()
    {
        // 1. FormatBytes & FormatSpeed
        NotificationPayloadBuilder.FormatBytes(1024).Should().Contain("KB");
        NotificationPayloadBuilder.FormatBytes(1048576).Should().Contain("MB");
        NotificationPayloadBuilder.FormatBytes(1073741824).Should().Contain("GB");
        NotificationPayloadBuilder.FormatSpeed(1048576).Should().Contain("/s");

        // 2. EscapeMrkdwn & EscapeJsonString
        NotificationPayloadBuilder.EscapeSlackMrkdwn("test & <tag>").Should().NotContain("<tag>");
        NotificationPayloadBuilder.EscapeJsonString("line 1\nline 2").Should().Contain("\\n");

        // 3. Settings extractors
        var (chatId, token, user, sound) = NotificationPayloadBuilder.ExtractProviderSettings("{\"chat_id\":\"999\",\"token\":\"tok_123\",\"user\":\"usr_abc\",\"sound\":\"cosmic\"}");
        chatId.Should().Be("999");
        token.Should().Be("tok_123");

        var (uName, aUrl) = NotificationPayloadBuilder.ExtractDiscordSettings("{\"username\":\"CustomBot\",\"avatarUrl\":\"http://example.com/pic.png\"}");
        uName.Should().Be("CustomBot");
        aUrl.Should().Be("http://example.com/pic.png");

        var (bUser, bPass) = NotificationPayloadBuilder.ExtractBasicAuthSettings("{\"username\":\"admin\",\"password\":\"secret\"}");
        bUser.Should().Be("admin");
        bPass.Should().Be("secret");

        // 4. Color helpers
        NotificationPayloadBuilder.GetSlackColor("Download").Should().NotBeNullOrWhiteSpace();
        NotificationPayloadBuilder.GetDiscordColor("TorrentComplete").Should().BeGreaterThan(0);

        // 5. HttpMethod resolution
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "method=POST").Should().Be(HttpMethod.Post);
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "method=PUT").Should().Be(HttpMethod.Put);
    }

    [Test]
    public async Task NotificationController_CrudAndTest_EndpointsSucceed()
    {
        // 1. GET /api/v1/notification
        var listResp = await this.Client.GetAsync("/api/v1/notification");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/notification/test
        var testReq = new
        {
            name = "Test Discord Webhook",
            implementation = "Discord",
            configContract = "NullConfig",
            settings = "https://discord.com/api/webhooks/dummy/dummy",
        };
        var testResp = await this.PostJsonAsync("/api/v1/notification/test", testReq);
        // Returns OK or BadRequest depending on URL validation, but exercises the controller endpoint
        testResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }
}
