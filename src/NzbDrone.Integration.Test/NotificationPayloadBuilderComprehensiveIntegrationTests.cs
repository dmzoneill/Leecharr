// Copyright (c) FeedItOut. All rights reserved.

using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class NotificationPayloadBuilderComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void NotificationPayloadBuilder_SettingsExtraction_HandlesAllFormats()
    {
        // 1. ExtractProviderSettings: JSON and URL query formats
        var jsonSettings = "{\"chatId\":\"998877\",\"token\":\"my-token-123\",\"user\":\"leecharr-user\",\"sound\":\"pianobar\"}";
        var (chatId1, token1, user1, sound1) = NotificationPayloadBuilder.ExtractProviderSettings(jsonSettings);
        chatId1.Should().Be("998877");
        token1.Should().Be("my-token-123");
        user1.Should().Be("leecharr-user");
        sound1.Should().Be("pianobar");

        var urlSettings = "chat_id=112233&token=tok-abc&user=usr-xyz&sound=cosmic";
        var (chatId2, token2, user2, sound2) = NotificationPayloadBuilder.ExtractProviderSettings(urlSettings);
        chatId2.Should().Be("112233");
        token2.Should().Be("tok-abc");
        user2.Should().Be("usr-xyz");
        sound2.Should().Be("cosmic");

        var (chatIdEmpty, tokenEmpty, userEmpty, soundEmpty) = NotificationPayloadBuilder.ExtractProviderSettings(string.Empty);
        chatIdEmpty.Should().BeNullOrEmpty();
        tokenEmpty.Should().BeNullOrEmpty();

        // 2. ExtractDiscordSettings
        var discordJson = "{\"username\":\"LeecharrAlerts\",\"avatarUrl\":\"https://example.com/logo.png\"}";
        var (discUser, discAvatar) = NotificationPayloadBuilder.ExtractDiscordSettings(discordJson);
        discUser.Should().Be("LeecharrAlerts");
        discAvatar.Should().Be("https://example.com/logo.png");

        var discordUrl = "username=CustomBot&avatarUrl=https://example.com/icon.png";
        var (discUser2, discAvatar2) = NotificationPayloadBuilder.ExtractDiscordSettings(discordUrl);
        discUser2.Should().Be("CustomBot");
        discAvatar2.Should().Be("https://example.com/icon.png");

        // 3. ExtractBasicAuthSettings
        var authJson = "{\"basicAuthUsername\":\"admin\",\"basicAuthPassword\":\"secret123\"}";
        var (uAuth, pAuth) = NotificationPayloadBuilder.ExtractBasicAuthSettings(authJson);
        uAuth.Should().Be("admin");
        pAuth.Should().Be("secret123");

        var authUrl = "username=root&password=toor";
        var (uAuth2, pAuth2) = NotificationPayloadBuilder.ExtractBasicAuthSettings(authUrl);
        uAuth2.Should().Be("root");
        pAuth2.Should().Be("toor");

        // 4. ExtractPushoverSettings
        var pushSettings = "{\"priority\":\"1\",\"retry\":\"60\",\"expire\":\"3600\",\"device\":\"pixel\",\"sound\":\"echo\"}";
        var (prio, retry, expire, dev, snd) = NotificationPayloadBuilder.ExtractPushoverSettings(pushSettings);
        prio.Should().Be(1);
        retry.Should().Be(60);
        expire.Should().Be(3600);
        dev.Should().Be("pixel");
        snd.Should().Be("echo");
    }

    [Test]
    public void NotificationPayloadBuilder_HttpAndHeaderResolution_WorksCorrectly()
    {
        // 1. ResolveHttpMethod for standard implementations
        NotificationPayloadBuilder.ResolveHttpMethod("Discord", null).Should().Be(HttpMethod.Post);
        NotificationPayloadBuilder.ResolveHttpMethod("Telegram", null).Should().Be(HttpMethod.Post);
        NotificationPayloadBuilder.ResolveHttpMethod("Pushover", null).Should().Be(HttpMethod.Post);
        NotificationPayloadBuilder.ResolveHttpMethod("Slack", null).Should().Be(HttpMethod.Post);
        NotificationPayloadBuilder.ResolveHttpMethod("Gotify", null).Should().Be(HttpMethod.Post);

        // 2. ResolveHttpMethod with custom method in settings
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"httpMethod\":\"PUT\"}").Should().Be(HttpMethod.Put);
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"httpMethod\":\"PATCH\"}").Should().Be(HttpMethod.Patch);
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "httpMethod=GET").Should().Be(HttpMethod.Get);

        // 3. ResolveTargetUrl
        var discUrl = NotificationPayloadBuilder.ResolveTargetUrl("Discord", "{\"url\":\"https://discord.com/api/webhooks/123/xyz\"}");
        discUrl.Should().Be("https://discord.com/api/webhooks/123/xyz");

        var teleUrl = NotificationPayloadBuilder.ResolveTargetUrl("Telegram", "{\"token\":\"bot12345:ABCDE\"}");
        teleUrl.Should().Contain("bot12345:ABCDE");

        // 4. ResolveCustomHeaders
        var headers = NotificationPayloadBuilder.ResolveCustomHeaders("Webhook", "{\"headers\":\"X-Custom: Value\\nX-Auth: Secret\"}");
        headers.Should().Contain("X-Custom: Value");
    }

    [Test]
    public void NotificationPayloadBuilder_FormattingAndColors_CalculatesCorrectly()
    {
        // 1. Colors
        NotificationPayloadBuilder.GetSlackColor("TorrentCompleted").Should().NotBeNullOrWhiteSpace();
        NotificationPayloadBuilder.GetSlackColor("HealthIssue").Should().NotBeNullOrWhiteSpace();
        NotificationPayloadBuilder.GetDiscordColor("TorrentCompleted").Should().BeGreaterThan(0);
        NotificationPayloadBuilder.GetDiscordColor("HealthIssue").Should().BeGreaterThan(0);

        // 2. Escape mrkdwn
        NotificationPayloadBuilder.EscapeSlackMrkdwn("Foo & Bar <tag>").Should().Be("Foo &amp; Bar &lt;tag&gt;");

        // 3. FormatBytes
        NotificationPayloadBuilder.FormatBytes(500).Should().Contain("B");
        NotificationPayloadBuilder.FormatBytes(1024 * 1024).Should().Contain("MB");
        NotificationPayloadBuilder.FormatBytes(1024L * 1024L * 1024L * 2).Should().Contain("GB");
        NotificationPayloadBuilder.FormatBytes(1024L * 1024L * 1024L * 1024L * 3).Should().Contain("TB");

        // 4. FormatSpeed
        NotificationPayloadBuilder.FormatSpeed(1024 * 1024 * 5).Should().Contain("MB/s");

        // 5. IsLikelyJson and EscapeJsonString
        NotificationPayloadBuilder.IsLikelyJson("{\"key\":\"value\"}").Should().BeTrue();
        NotificationPayloadBuilder.IsLikelyJson("[1, 2, 3]").Should().BeTrue();
        NotificationPayloadBuilder.IsLikelyJson("Hello plain text").Should().BeFalse();
        NotificationPayloadBuilder.EscapeJsonString("Line1\nLine2\"quote\"").Should().Contain("\\n").And.Contain("\\\"");
    }

    [Test]
    public void NotificationPayloadBuilder_TemplateInterpolation_ReplacesAllTokens()
    {
        var torrent = new Torrent
        {
            Id = 77,
            Name = "Inception.2010.1080p.BluRay",
            InfoHash = "4444444444444444444444444444444444444444",
            Category = "movies",
            Status = TorrentStatus.Seeding,
            TotalSize = 104857600,
            Downloaded = 104857600,
            Uploaded = 209715200,
            Ratio = 2.0,
            Progress = 1.0,
            DownloadSpeed = 0,
            UploadSpeed = 1048576,
        };

        var template = "Event: {eventType} | Name: {torrent.name} | Category: {torrent.category} | Size: {torrent.size} | Ratio: {torrent.ratio} | Progress: {torrent.progresspercent}% | UpSpeed: {torrent.uploadspeedformatted}";
        var interpolated = NotificationPayloadBuilder.InterpolateTemplate(
            template,
            eventType: "TorrentDownloadCompleted",
            torrent: torrent);

        interpolated.Should().Contain("TorrentDownloadCompleted");
        interpolated.Should().Contain("Inception.2010.1080p.BluRay");
        interpolated.Should().Contain("movies");
        interpolated.Should().Contain("2");
        interpolated.Should().Contain("100%");
        interpolated.Should().Contain("MB/s");

        // JSON template with auto-escaping
        var jsonTemplate = "{\"event\":\"{eventType}\",\"title\":\"{torrent.name}\",\"hash\":\"{torrent.infohash}\"}";
        var jsonResult = NotificationPayloadBuilder.InterpolateTemplate(
            jsonTemplate,
            eventType: "TorrentAdded",
            torrent: torrent);
        jsonResult.Should().Contain("\"event\":\"TorrentAdded\"");
        jsonResult.Should().Contain("\"title\":\"Inception.2010.1080p.BluRay\"");
        jsonResult.Should().Contain("\"hash\":\"4444444444444444444444444444444444444444\"");
    }

    [Test]
    public void NotificationPayloadBuilder_BuildProviderPayload_GeneratesValidStructures()
    {
        var torrent = new Torrent
        {
            Id = 88,
            Name = "The.Matrix.1999.2160p.UHD",
            Category = "scifi",
            Status = TorrentStatus.Downloading,
            TotalSize = 524288000,
            Progress = 0.65,
        };

        var meta = new TorrentMediaMetadata
        {
            Overview = "A computer hacker learns from mysterious rebels about the true nature of his reality.",
        };

        // 1. Discord
        var discordSettings = "{\"username\":\"LeecharrMatrix\",\"avatarUrl\":\"https://example.com/matrix.png\"}";
        var discPayload = NotificationPayloadBuilder.BuildProviderPayload("Discord", "TorrentAdded", torrent, meta, null, discordSettings);
        discPayload.Should().NotBeNull();
        var discJson = JsonSerializer.Serialize(discPayload);
        discJson.Should().Contain("LeecharrMatrix");
        discJson.Should().Contain("The.Matrix.1999.2160p.UHD");

        // 2. Slack
        var slackPayload = NotificationPayloadBuilder.BuildProviderPayload("Slack", "TorrentDownloadCompleted", torrent, meta, null);
        slackPayload.Should().NotBeNull();
        var slackJson = JsonSerializer.Serialize(slackPayload);
        slackJson.Should().Contain("The.Matrix.1999.2160p.UHD");
        slackJson.Should().Contain("blocks");

        // 3. Telegram
        var teleSettings = "{\"chatId\":\"778899\"}";
        var telePayload = NotificationPayloadBuilder.BuildProviderPayload("Telegram", "TorrentDownloadCompleted", torrent, meta, null, teleSettings);
        telePayload.Should().NotBeNull();
        var teleJson = JsonSerializer.Serialize(telePayload);
        teleJson.Should().Contain("778899");

        // 4. Gotify
        var gotifyPayload = NotificationPayloadBuilder.BuildProviderPayload("Gotify", "TorrentAdded", torrent, meta, null);
        gotifyPayload.Should().NotBeNull();
        var gotifyJson = JsonSerializer.Serialize(gotifyPayload);
        gotifyJson.Should().Contain("Leecharr: TorrentAdded");

        // 5. Pushover
        var pushoverSettings = "{\"token\":\"po-tok\",\"user\":\"po-user\",\"sound\":\"siren\"}";
        var pushPayload = NotificationPayloadBuilder.BuildProviderPayload("Pushover", "HealthIssue", torrent, meta, null, pushoverSettings);
        pushPayload.Should().NotBeNull();
        var pushJson = JsonSerializer.Serialize(pushPayload);
        pushJson.Should().Contain("po-tok");
        pushJson.Should().Contain("siren");

        // 6. Apprise
        var apprisePayload = NotificationPayloadBuilder.BuildProviderPayload("Apprise", "TorrentDownloadCompleted", torrent, meta, null);
        apprisePayload.Should().NotBeNull();
        var appriseJson = JsonSerializer.Serialize(apprisePayload);
        appriseJson.Should().Contain("Leecharr: TorrentDownloadCompleted");

        // 7. Custom Webhook template
        var webhookSettings = "{\"payloadTemplate\":\"{\\\"type\\\":\\\"{eventType}\\\",\\\"name\\\":\\\"{torrent.name}\\\"}\"}";
        var hookPayload = NotificationPayloadBuilder.BuildProviderPayload("Webhook", "TorrentAdded", torrent, meta, null, webhookSettings);
        hookPayload.Should().NotBeNull();
        hookPayload.ToString().Should().Contain("TorrentAdded");
        hookPayload.ToString().Should().Contain("The.Matrix.1999.2160p.UHD");
    }
}
