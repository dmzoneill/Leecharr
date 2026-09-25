// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class NotificationPayloadBuilderTest
{
    [TestCase("http://gotify-server:8080", "http://gotify-server:8080/message")]
    [TestCase("http://gotify-server:8080/", "http://gotify-server:8080/message")]
    [TestCase("http://gotify-server:8080/message", "http://gotify-server:8080/message")]
    [TestCase("http://gotify-server:8080/message/", "http://gotify-server:8080/message")]
    [TestCase("http://gotify-server:8080/gotify", "http://gotify-server:8080/gotify/message")]
    [TestCase("http://gotify-server:8080/gotify/", "http://gotify-server:8080/gotify/message")]
    [TestCase("http://gotify-server:8080/gotify/message", "http://gotify-server:8080/gotify/message")]
    [TestCase("http://gotify-server:8080/gotify/message/", "http://gotify-server:8080/gotify/message")]
    [TestCase("http://gotify-server:8080?token=SecretToken123", "http://gotify-server:8080/message?token=SecretToken123")]
    [TestCase("http://gotify-server:8080/?token=SecretToken123", "http://gotify-server:8080/message?token=SecretToken123")]
    [TestCase("http://gotify-server:8080/message?token=SecretToken123", "http://gotify-server:8080/message?token=SecretToken123")]
    [TestCase("http://gotify-server:8080/message/?token=SecretToken123", "http://gotify-server:8080/message?token=SecretToken123")]
    [TestCase("http://gotify-server:8080/gotify?token=SecretToken123", "http://gotify-server:8080/gotify/message?token=SecretToken123")]
    [TestCase("http://gotify-server:8080/gotify/?token=SecretToken123", "http://gotify-server:8080/gotify/message?token=SecretToken123")]
    [TestCase("http://gotify-server:8080/gotify/message?token=SecretToken123", "http://gotify-server:8080/gotify/message?token=SecretToken123")]
    [TestCase("https://gotify-server:8443/custom/?token=SecretToken123&priority=high#section", "https://gotify-server:8443/custom/message?token=SecretToken123&priority=high#section")]
    public void ResolveTargetUrl_Gotify_EndpointsAreNormalizedWithoutCorruptingQueryOrPath(string inputUrl, string expectedUrl)
    {
        var resolved = NotificationPayloadBuilder.ResolveTargetUrl("Gotify", inputUrl);
        resolved.Should().Be(expectedUrl);
    }

    [Test]
    public void ResolveTargetUrl_Gotify_FromJsonSettings_PreservesQueryString()
    {
        var jsonSettings = "{\"url\":\"http://gotify-server:8080?token=SecretToken123\"}";
        var resolved = NotificationPayloadBuilder.ResolveTargetUrl("Gotify", jsonSettings);
        resolved.Should().Be("http://gotify-server:8080/message?token=SecretToken123");

        var serverUrlSettings = "{\"serverUrl\":\"http://gotify-server:8080/?token=SecretToken123\"}";
        var serverUrlResolved = NotificationPayloadBuilder.ResolveTargetUrl("Gotify", serverUrlSettings);
        serverUrlResolved.Should().Be("http://gotify-server:8080/message?token=SecretToken123");
    }

    [TestCase("http://apprise-server:8000", "http://apprise-server:8000/notify")]
    [TestCase("http://apprise-server:8000/", "http://apprise-server:8000/notify")]
    [TestCase("http://apprise-server:8000/notify", "http://apprise-server:8000/notify")]
    [TestCase("http://apprise-server:8000/notify/", "http://apprise-server:8000/notify")]
    [TestCase("http://apprise-server:8000/apprise", "http://apprise-server:8000/apprise/notify")]
    [TestCase("http://apprise-server:8000/apprise/", "http://apprise-server:8000/apprise/notify")]
    [TestCase("http://apprise-server:8000/apprise/notify", "http://apprise-server:8000/apprise/notify")]
    [TestCase("http://apprise-server:8000/apprise/notify/", "http://apprise-server:8000/apprise/notify")]
    [TestCase("http://apprise-server:8000?tags=media", "http://apprise-server:8000/notify?tags=media")]
    [TestCase("http://apprise-server:8000/?tags=media", "http://apprise-server:8000/notify?tags=media")]
    [TestCase("http://apprise-server:8000/notify?tags=media", "http://apprise-server:8000/notify?tags=media")]
    [TestCase("http://apprise-server:8000/notify/?tags=media", "http://apprise-server:8000/notify?tags=media")]
    [TestCase("http://apprise-server:8000/apprise?tags=media", "http://apprise-server:8000/apprise/notify?tags=media")]
    [TestCase("http://apprise-server:8000/apprise/?tags=media", "http://apprise-server:8000/apprise/notify?tags=media")]
    [TestCase("http://apprise-server:8000/apprise/notify?tags=media", "http://apprise-server:8000/apprise/notify?tags=media")]
    [TestCase("https://apprise-server:8443/custom/?tags=media&format=markdown#section", "https://apprise-server:8443/custom/notify?tags=media&format=markdown#section")]
    public void ResolveTargetUrl_Apprise_EndpointsAreNormalizedWithoutCorruptingQueryOrPath(string inputUrl, string expectedUrl)
    {
        var resolved = NotificationPayloadBuilder.ResolveTargetUrl("Apprise", inputUrl);
        resolved.Should().Be(expectedUrl);
    }

    [Test]
    public void ResolveTargetUrl_Apprise_FromJsonSettings_PreservesQueryString()
    {
        var jsonSettings = "{\"url\":\"http://apprise-server:8000?tags=media\"}";
        var resolved = NotificationPayloadBuilder.ResolveTargetUrl("Apprise", jsonSettings);
        resolved.Should().Be("http://apprise-server:8000/notify?tags=media");
    }

    [Test]
    public void BuildProviderPayload_Pushover_TruncatesMessageTo1024AndTitleTo250()
    {
        var longMessage = new string('A', 2000);
        var longTitle = new string('T', 300);
        var genericPayload = new Dictionary<string, object>
        {
            ["Message"] = longMessage,
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Pushover", longTitle, null, null, genericPayload, null);
        var payloadDict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        var message = payloadDict["message"].ToString();
        var title = payloadDict["title"].ToString();

        message.Should().NotBeNull();
        message!.Length.Should().BeLessThanOrEqualTo(1024);
        message.Should().EndWith("...");

        title.Should().NotBeNull();
        title!.Length.Should().BeLessThanOrEqualTo(250);
        title.Should().EndWith("...");
    }

    [Test]
    public void BuildProviderPayload_Pushover_WithinLimitsDoesNotTruncate()
    {
        var shortMessage = "Short notification message";
        var genericPayload = new Dictionary<string, object>
        {
            ["Message"] = shortMessage,
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Pushover", "OnGrab", null, null, genericPayload, null);
        var payloadDict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        payloadDict["message"].ToString().Should().Be(shortMessage);
        payloadDict["title"].ToString().Should().Be("Leecharr: OnGrab");
    }

    [TestCase("OnHealthIssue", 8, 1)]
    [TestCase("OnManualInteractionRequired", 8, 1)]
    [TestCase("OnDownloadComplete", 5, 0)]
    [TestCase("OnSeedGoalReached", 5, 0)]
    [TestCase("OnExtractComplete", 5, 0)]
    [TestCase("OnGrab", 3, -1)]
    [TestCase("OnMediaInspected", 3, -1)]
    [TestCase("OnTorrentDeleted", 3, -1)]
    [TestCase("OnUnknownEvent", 5, 0)]
    public void BuildProviderPayload_GotifyAndPushover_MapPrioritiesAcrossEventTypes(string eventType, int expectedGotifyPriority, int expectedPushoverPriority)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Torrent",
            Category = "Linux",
            Status = TorrentStatus.Downloading,
        };

        // Gotify
        var gotifyPayload = NotificationPayloadBuilder.BuildProviderPayload("Gotify", eventType, torrent, null, null, null);
        var gotifyJson = JsonSerializer.Serialize(gotifyPayload);
        using var gotifyDoc = JsonDocument.Parse(gotifyJson);
        gotifyDoc.RootElement.GetProperty("priority").GetInt32().Should().Be(expectedGotifyPriority);

        // Pushover
        var pushoverPayload = NotificationPayloadBuilder.BuildProviderPayload("Pushover", eventType, torrent, null, null, null);
        var pushoverDict = pushoverPayload.Should().BeOfType<Dictionary<string, object>>().Subject;
        pushoverDict["priority"].Should().Be(expectedPushoverPriority);
    }

    [TestCase("{\"sound\":\"cosmic\"}", "cosmic")]
    [TestCase("sound=pianobar", "pianobar")]
    [TestCase("sound=spacealarm&token=token123", "spacealarm")]
    public void BuildProviderPayload_Pushover_PopulatesSoundIfConfigured(string settings, string expectedSound)
    {
        var result = NotificationPayloadBuilder.BuildProviderPayload("Pushover", "OnDownloadComplete", null, null, "Download completed", settings);
        var payloadDict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        payloadDict.Should().ContainKey("sound");
        payloadDict["sound"].Should().Be(expectedSound);
    }

    [Test]
    public void BuildProviderPayload_Pushover_OmitsSoundWhenNotConfigured()
    {
        var result = NotificationPayloadBuilder.BuildProviderPayload("Pushover", "OnDownloadComplete", null, null, "Download completed", "{\"token\":\"tok123\"}");
        var payloadDict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        payloadDict.Should().NotContainKey("sound");
    }

    [Test]
    public void BuildProviderPayload_Slack_IncludesBlocksStructureWithHeaderSectionAndContext()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Movie.Name.2024.1080p",
            Category = "Movies",
            Status = TorrentStatus.Seeding,
            TotalSize = 1024 * 1024 * 500, // 500 MB
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Slack", "OnDownloadComplete", torrent, null, null, null);
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("text", out var textProp).Should().BeTrue();
        textProp.GetString().Should().Contain("Movie.Name.2024.1080p");

        root.TryGetProperty("username", out var userProp).Should().BeTrue();
        userProp.GetString().Should().Be("Leecharr");

        root.TryGetProperty("blocks", out var blocksProp).Should().BeTrue();
        blocksProp.ValueKind.Should().Be(JsonValueKind.Array);
        var blocks = blocksProp.EnumerateArray().ToList();
        blocks.Count.Should().Be(3);

        // Header block
        blocks[0].GetProperty("type").GetString().Should().Be("header");
        blocks[0].GetProperty("text").GetProperty("type").GetString().Should().Be("plain_text");
        blocks[0].GetProperty("text").GetProperty("text").GetString().Should().Contain("OnDownloadComplete");

        // Section block with fields
        blocks[1].GetProperty("type").GetString().Should().Be("section");
        var fields = blocks[1].GetProperty("fields").EnumerateArray().ToList();
        fields.Count.Should().Be(4);
        fields[0].GetProperty("text").GetString().Should().Contain("Movie.Name.2024.1080p");
        fields[1].GetProperty("text").GetString().Should().Contain("Movies");
        fields[2].GetProperty("text").GetString().Should().Contain("Seeding");

        // Context block
        blocks[2].GetProperty("type").GetString().Should().Be("context");
        var elements = blocks[2].GetProperty("elements").EnumerateArray().ToList();
        elements.Count.Should().Be(1);
        elements[0].GetProperty("text").GetString().Should().Contain("Leecharr");
    }

    [Test]
    public void BuildProviderPayload_Telegram_DoesNotEscapeParenthesesInLegacyMarkdown()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Ubuntu.Linux.(2024).[x86_64].iso",
            Category = "Linux (Distros)",
            Status = TorrentStatus.Downloading,
            Progress = 0.75,
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Telegram", "OnGrab", torrent, null, null, "{\"token\":\"tok\",\"chat_id\":\"12345\"}");
        var dict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        dict["parse_mode"].Should().Be("Markdown");
        dict["chat_id"].Should().Be("12345");

        var text = dict["text"].ToString();
        text.Should().NotBeNull();

        // Parentheses must NOT be escaped
        text.Should().Contain("(2024)");
        text.Should().Contain("Linux (Distros)");
        text.Should().NotContain(@"\(");
        text.Should().NotContain(@"\)");

        // Reserved characters like [ and ] must be escaped
        text.Should().Contain(@"\[x86\_64\]");
    }

    [Test]
    public void BuildProviderPayload_Telegram_TruncationClosesEntitiesWithoutBrokenBackslashes()
    {
        var veryLongName = "Ubuntu_" + new string('A', 5000);
        var torrent = new Torrent
        {
            Id = 1,
            Name = veryLongName,
            Category = "OS",
            Status = TorrentStatus.Downloading,
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Telegram", "OnGrab", torrent, null, null, null);
        var dict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        var text = dict["text"].ToString();
        text.Should().NotBeNull();
        text!.Length.Should().BeLessThanOrEqualTo(4096);

        // Verify trailing escape backslashes are not left dangling
        text.Should().NotEndWith(@"\");
        text.Should().NotContain(@"\...");

        // Verify opened bold entity is closed
        text.Should().EndWith("*");
        NotificationPayloadBuilder.GetClosingTags(text, text.Length).Should().BeEmpty();
    }

    [TestCase("Hello World", 50, "Hello World")]
    [TestCase("*Bold text*", 50, "*Bold text*")]
    [TestCase("*Unclosed bold text that is very long", 20, "*Unclosed bold t...*")]
    [TestCase("_Unclosed italic that is very long", 20, "_Unclosed italic..._")]
    [TestCase("`Unclosed code that is very long", 20, "`Unclosed code t...`")]
    [TestCase("Trailing backslash\\", 20, "Trailing backslash")]
    public void TruncateTelegramMarkdown_CorrectlyTruncatesAndClosesEntities(string input, int maxLen, string expected)
    {
        var result = NotificationPayloadBuilder.TruncateTelegramMarkdown(input, maxLen);
        result.Should().Be(expected);
    }

    [Test]
    public void BuildProviderPayload_Discord_TruncatesDescriptionTo2048Characters()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Movie.Name.2024",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1024 * 1024 * 100,
        };

        var longOverview = new string('O', 3000);
        var meta = new TorrentMediaMetadata { Overview = longOverview };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Discord", "OnGrab", torrent, meta, null, null);
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var embeds = doc.RootElement.GetProperty("embeds");
        embeds.GetArrayLength().Should().Be(1);

        var description = embeds[0].GetProperty("description").GetString();
        description.Should().NotBeNull();
        description!.Length.Should().BeLessThanOrEqualTo(2048);
        description.Should().EndWith("...");
    }

    [Test]
    public void BuildProviderPayload_Telegram_EscapesSquareBracketsAndStatus()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Release_Name_2024",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.42,
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload("Telegram", "OnGrab", torrent, null, null, null);
        var dict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        var text = dict["text"].ToString();
        text.Should().NotBeNull();
        text.Should().Contain(@"*Leecharr \[OnGrab\]*");
        text.Should().Contain("Status: Downloading");
    }

    [Test]
    public void BuildProviderPayload_Telegram_WithoutTorrent_EscapesSquareBrackets()
    {
        var result = NotificationPayloadBuilder.BuildProviderPayload("Telegram", "OnHealthIssue", null, null, "Health check failed", null);
        var dict = result.Should().BeOfType<Dictionary<string, object>>().Subject;

        var text = dict["text"].ToString();
        text.Should().NotBeNull();
        text.Should().Contain(@"*Leecharr \[OnHealthIssue\]*");
    }
}
