// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

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
}
