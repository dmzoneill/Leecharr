// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CoreAndApiDeepCoverageIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task SystemAndHealthEndpoints_GetRequests_ReturnsOkResponses()
    {
        // 1. System status
        var statusResp = await this.Client.GetAsync("/api/v1/system/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusJson);
        statusDoc.RootElement.GetProperty("appName").GetString().Should().Be("Leecharr");

        // 2. Health checks
        var healthResp = await this.Client.GetAsync("/api/v1/health");
        healthResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Disk space
        var diskResp = await this.Client.GetAsync("/api/v1/diskspace");
        diskResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ConfigurationEndpoints_GetAndQuery_ReturnsConfigurationModels()
    {
        // 1. General config
        var generalResp = await this.Client.GetAsync("/api/v1/config/general");
        generalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var generalJson = await generalResp.Content.ReadAsStringAsync();
        using var generalDoc = JsonDocument.Parse(generalJson);
        generalDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        // 2. Advanced config
        var advResp = await this.Client.GetAsync("/api/v1/config/advanced");
        advResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. BitTorrent config
        var btResp = await this.Client.GetAsync("/api/v1/config/bittorrent");
        btResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Network config
        var netResp = await this.Client.GetAsync("/api/v1/config/network");
        netResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public void NotificationPayloadBuilder_SettingsExtraction_ExtractsAllProviderConfigs()
    {
        // 1. ExtractProviderSettings - JSON format
        var jsonSettings = "{\"chat_id\":\"123456789\",\"token\":\"bot1234:token_abc\",\"user\":\"user_key_xyz\",\"sound\":\"cosmic\"}";
        var (chatId, token, user, sound) = NotificationPayloadBuilder.ExtractProviderSettings(jsonSettings);
        chatId.Should().Be("123456789");
        token.Should().Be("bot1234:token_abc");
        user.Should().Be("user_key_xyz");
        sound.Should().Be("cosmic");

        // 2. ExtractProviderSettings - Query string format
        var querySettings = "chat_id=987654321&token=sec_token_99&user=usr_77&sound=magic";
        var (qChatId, qToken, qUser, qSound) = NotificationPayloadBuilder.ExtractProviderSettings(querySettings);
        qChatId.Should().Be("987654321");
        qToken.Should().Be("sec_token_99");
        qUser.Should().Be("usr_77");
        qSound.Should().Be("magic");

        // 3. ExtractDiscordSettings
        var discordJson = "{\"username\":\"CustomBot\",\"avatarUrl\":\"https://cdn.example.org/avatar.png\"}";
        var (discUser, discAvatar) = NotificationPayloadBuilder.ExtractDiscordSettings(discordJson);
        discUser.Should().Be("CustomBot");
        discAvatar.Should().Be("https://cdn.example.org/avatar.png");

        // 4. ExtractBasicAuthSettings
        var authJson = "{\"username\":\"admin_user\",\"password\":\"secure_pass_42\"}";
        var (authUser, authPass) = NotificationPayloadBuilder.ExtractBasicAuthSettings(authJson);
        authUser.Should().Be("admin_user");
        authPass.Should().Be("secure_pass_42");

        // 5. ExtractPushoverSettings
        var pushoverJson = "{\"priority\":1,\"retry\":120,\"expire\":7200,\"device\":\"phone\",\"sound\":\"upbeat\"}";
        var (prio, retry, expire, device, poSound) = NotificationPayloadBuilder.ExtractPushoverSettings(pushoverJson);
        prio.Should().Be(1);
        retry.Should().Be(120);
        expire.Should().Be(7200);
        device.Should().Be("phone");
        poSound.Should().Be("upbeat");

        // 6. ExtractPriority and ExtractPayloadTemplate
        NotificationPayloadBuilder.ExtractPriority("{\"priority\":2}", defaultPriority: 0).Should().Be(2);
        NotificationPayloadBuilder.ExtractPayloadTemplate("{\"payloadTemplate\":\"{{torrent.name}} completed\"}").Should().Be("{{torrent.name}} completed");

        // 7. Colors and markdown escaping
        NotificationPayloadBuilder.GetSlackColor("DownloadCompleted").Should().Be("#2EB67D");
        NotificationPayloadBuilder.GetSlackColor("DownloadFailed").Should().Be("#E01E5A");
        NotificationPayloadBuilder.GetDiscordColor("DownloadCompleted").Should().Be(0x57F287);
        NotificationPayloadBuilder.GetDiscordColor("DownloadFailed").Should().Be(0xED4245);
        NotificationPayloadBuilder.EscapeSlackMrkdwn("<tag>&text>").Should().Be("&lt;tag&gt;&amp;text&gt;");
    }

    [Test]
    public void NotificationPayloadBuilder_BuildProviderPayload_GeneratesPayloadsForVariousProviders()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "OpenSourceSoftware.iso",
            Category = "Linux",
            Status = TorrentStatus.Downloading,
            Progress = 0.85,
            TotalSize = 2_000_000_000,
        };

        // 1. Discord payload
        var discordPayload = NotificationPayloadBuilder.BuildProviderPayload(
            "Discord",
            "TorrentAdded",
            torrent,
            meta: null,
            genericPayload: null,
            settings: "{\"username\":\"LeechBot\"}");
        discordPayload.Should().NotBeNull();

        // 2. Slack payload
        var slackPayload = NotificationPayloadBuilder.BuildProviderPayload(
            "Slack",
            "TorrentComplete",
            torrent,
            meta: null,
            genericPayload: null);
        slackPayload.Should().NotBeNull();

        // 3. Telegram payload
        var telegramPayload = NotificationPayloadBuilder.BuildProviderPayload(
            "Telegram",
            "DownloadFailed",
            torrent,
            meta: null,
            genericPayload: null,
            settings: "chat_id=12345&token=bot123");
        telegramPayload.Should().NotBeNull();

        // 4. Gotify payload
        var gotifyPayload = NotificationPayloadBuilder.BuildProviderPayload(
            "Gotify",
            "TorrentAdded",
            torrent,
            meta: null,
            genericPayload: null);
        gotifyPayload.Should().NotBeNull();

        // 5. Pushover payload
        var pushoverPayload = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            "TorrentAdded",
            torrent,
            meta: null,
            genericPayload: null,
            settings: "token=app_tok&user=usr_tok");
        pushoverPayload.Should().NotBeNull();

        // 6. Apprise payload
        var apprisePayload = NotificationPayloadBuilder.BuildProviderPayload(
            "Apprise",
            "HealthIssue",
            torrent: null,
            meta: null,
            genericPayload: new { message = "Disk warning" });
        apprisePayload.Should().NotBeNull();
    }

    [Test]
    public void NotificationPayloadBuilder_ResolveTargetUrlAndHeaders_ResolvesEndpoints()
    {
        // 1. Telegram target URL
        var tgUrl = NotificationPayloadBuilder.ResolveTargetUrl("Telegram", "token=secret_telegram_token");
        tgUrl.Should().Be("https://api.telegram.org/botsecret_telegram_token/sendMessage");

        // 2. Pushover target URL
        var poUrl = NotificationPayloadBuilder.ResolveTargetUrl("Pushover", "user=key");
        poUrl.Should().Be("https://api.pushover.net/1/messages.json");

        // 3. Apprise notify URL append
        var apUrl = NotificationPayloadBuilder.ResolveTargetUrl("Apprise", "{\"url\":\"https://apprise.lan/\"}");
        apUrl.Should().Contain("notify");

        // 4. Gotify message URL append
        var goUrl = NotificationPayloadBuilder.ResolveTargetUrl("Gotify", "{\"url\":\"https://gotify.lan/\"}");
        goUrl.Should().Contain("message");

        // 5. ResolveCustomHeaders with Gotify token insertion
        var gotifyHeaders = NotificationPayloadBuilder.ResolveCustomHeaders("Gotify", "{\"token\":\"gotify_token_456\"}");
        gotifyHeaders.Should().Contain("X-Gotify-Key");
        gotifyHeaders.Should().Contain("gotify_token_456");

        // 6. ResolveHttpMethod
        NotificationPayloadBuilder.ResolveHttpMethod("Telegram", null).Should().Be(HttpMethod.Post);
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"GET\"}").Should().Be(HttpMethod.Get);
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"PUT\"}").Should().Be(HttpMethod.Put);
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"PATCH\"}").Should().Be(HttpMethod.Patch);
    }

    [Test]
    public void YamlScriptRunner_ExecuteWorkflow_EvaluatesConditionsAndVariables()
    {
        var runner = new YamlScriptRunner();

        // 1. Empty YAML workflow
        var emptyScript = new AutomationScript
        {
            Name = "Empty Workflow",
            Code = "",
        };
        var emptyResult = runner.Execute(emptyScript);
        emptyResult.Success.Should().BeTrue();

        // 2. Workflow with merged inputs and log steps
        var yamlCode = "steps:\n  - id: step1\n    type: log\n    message: \"Executing workflow for {{inputs.targetSystem}}\"\n";
        var script = new AutomationScript
        {
            Name = "Logging Workflow",
            Code = yamlCode,
            InputsJson = "{\"targetSystem\":\"LeecharrIntegrationTest\"}",
        };

        var torrent = new Torrent
        {
            Id = 5,
            Name = "Ubuntu-24.04-desktop-amd64.iso",
            Category = "Linux",
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            TotalSize = 4_000_000_000,
        };

        var result = runner.Execute(script, torrent);
        result.Success.Should().BeTrue();
    }

    [Test]
    public void FileNameBuilder_BuildFileName_ReplacesTokensAndCleansString()
    {
        var builder = new FileNameBuilder();
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "Pilot" },
            Quality = "Bluray-1080p",
            ReleaseGroup = "LEAD",
            Extension = ".mkv",
        };

        var template = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} [{Quality Title}] - {Release Group}";
        var result = builder.BuildFileName(context, template);

        result.Should().Be("Breaking Bad - S01E01 - Pilot [Bluray-1080p] - LEAD.mkv");
    }
}
