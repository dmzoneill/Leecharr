// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class NotificationPayloadBuilderComprehensiveBranchTest
{
    private static readonly string[] EventTypes =
    {
        "Grab",
        "DownloadComplete",
        "SeedGoalReached",
        "HealthIssue",
        "HealthRestored",
        "TorrentDeleted",
        "TorrentError",
        "ManualInteractionRequired",
        "ApplicationUpdate",
        "Test",
    };

    private static readonly string[] Providers =
    {
        "Discord",
        "Telegram",
        "Slack",
        "Pushover",
        "Gotify",
        "Apprise",
    };

    [Test]
    public void BuildProviderPayload_AcrossAllProvidersAndEventTypes_GeneratesValidObjects()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Inception.2010.1080p.BluRay.x264",
            Category = "movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.75,
            TotalSize = 1024L * 1024 * 1024 * 4, // 4 GB
            Downloaded = 1024L * 1024 * 1024 * 3,
            Uploaded = 1024L * 1024 * 500,
            Ratio = 0.16,
            SavePath = "/downloads/movies",
            DateAdded = DateTime.UtcNow.AddHours(-2),
        };

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 10,
            Title = "Inception",
            Year = 2010,
            Overview = "A thief who steals corporate secrets through the use of dream-sharing technology.",
            TmdbId = "27205",
            ImdbId = "tt1375666",
            Genres = "Action, Sci-Fi",
            Rating = 8.8,
        };

        var settings = JsonSerializer.Serialize(new
        {
            url = "https://example.com/slack/T00/B00/X00",
            username = "LeecharrBot",
            avatarUrl = "https://leecharr.local/icon.png",
            chat_id = "123456789",
            token = "bot-secret-token",
            user = "user-secret-key",
            sound = "cosmic",
            channel = "#downloads",
            from = "notifications@leecharr.local",
            to = "admin@example.com",
            method = "POST",
            headers = "X-Custom-Alert: true\nAuthorization: Bearer test",
        });

        foreach (var provider in Providers)
        {
            foreach (var evt in EventTypes)
            {
                var payload = NotificationPayloadBuilder.BuildProviderPayload(provider, evt, torrent, meta, null, settings);
                payload.Should().NotBeNull($"Provider {provider} should return a non-null payload for event {evt}");

                // Also test with null metadata
                var payloadNoMeta = NotificationPayloadBuilder.BuildProviderPayload(provider, evt, torrent, null, null, settings);
                payloadNoMeta.Should().NotBeNull();

                // Also test with null torrent (generic system event)
                var genericMsg = new { Message = "Health check failed: disk nearly full", Status = "Warning" };
                var payloadGeneric = NotificationPayloadBuilder.BuildProviderPayload(provider, evt, null, null, genericMsg, settings);
                payloadGeneric.Should().NotBeNull();
            }
        }
    }

    [Test]
    public void ExtractProviderSettings_VariousFormats_ExtractsCorrectly()
    {
        // 1. JSON format with different property casing
        var jsonSettings = "{\"chatId\": \"999\", \"botToken\": \"tok123\", \"userKey\": \"usr456\", \"Sound\": \"siren\"}";
        var (chatId, token, user, sound) = NotificationPayloadBuilder.ExtractProviderSettings(jsonSettings);
        chatId.Should().Be("999");
        token.Should().Be("tok123");
        user.Should().Be("usr456");
        sound.Should().Be("siren");

        // 2. Query string format
        var qsSettings = "chat_id=888&token=tok789&user=usr321&sound=magic";
        var (c2, t2, u2, s2) = NotificationPayloadBuilder.ExtractProviderSettings(qsSettings);
        c2.Should().Be("888");
        t2.Should().Be("tok789");
        u2.Should().Be("usr321");
        s2.Should().Be("magic");

        // 3. Null and empty
        var (c3, t3, u3, s3) = NotificationPayloadBuilder.ExtractProviderSettings(null);
        c3.Should().BeEmpty();
        t3.Should().BeEmpty();
        u3.Should().BeEmpty();
        s3.Should().BeEmpty();
    }

    [Test]
    public void ResolveHttpMethod_SupportsAllCommonMethods()
    {
        NotificationPayloadBuilder.ResolveHttpMethod("Pushover", "").Method.Should().Be("POST");
        NotificationPayloadBuilder.ResolveHttpMethod("Discord", "").Method.Should().Be("POST");
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"GET\"}").Method.Should().Be("GET");
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"PUT\"}").Method.Should().Be("PUT");
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"PATCH\"}").Method.Should().Be("PATCH");
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "{\"method\":\"POST\"}").Method.Should().Be("POST");
        NotificationPayloadBuilder.ResolveHttpMethod("Webhook", "").Method.Should().Be("POST");
    }

    [Test]
    public void ExtractDiscordSettings_HandlesCustomAndDefaultSettings()
    {
        var (userDefault, avatarDefault) = NotificationPayloadBuilder.ExtractDiscordSettings(null);
        userDefault.Should().Be("Leecharr");
        avatarDefault.Should().BeNull();

        var (userCustom, avatarCustom) = NotificationPayloadBuilder.ExtractDiscordSettings("{\"username\": \"CustomBot\", \"avatarUrl\": \"http://img.png\"}");
        userCustom.Should().Be("CustomBot");
    }
}
