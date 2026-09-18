// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Trackers;

[TestFixture]
public class TrackerUrlSanitizerTest
{
    [Test]
    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase("   ", "   ")]
    public void Sanitize_WhenNullOrWhitespace_ReturnsOriginal(string input, string expected)
    {
        TrackerUrlSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Test]
    [TestCase("udp://tracker.opentrackr.org:1337/announce")]
    [TestCase("http://tracker.files.fm:6969/announce")]
    [TestCase("https://tracker.tamersunion.org:443/announce")]
    [TestCase("http://tracker.example.com/announce.php")]
    [TestCase("http://tracker.example.com/a/announce")]
    public void Sanitize_WhenPublicTracker_DoesNotAlterUrl(string publicUrl)
    {
        TrackerUrlSanitizer.Sanitize(publicUrl).Should().Be(publicUrl);
    }

    [Test]
    [TestCase("https://tracker.site/abcdef123456/announce", "https://tracker.site/********/announce")]
    [TestCase("https://tracker.site/announce/abcdef123456", "https://tracker.site/announce/********")]
    [TestCase("https://gazelle.tracker.org/0123456789abcdef0123456789abcdef/announce", "https://gazelle.tracker.org/********/announce")]
    [TestCase("https://ptp.tracker.org/announce/0123456789abcdef0123456789abcdef", "https://ptp.tracker.org/announce/********")]
    [TestCase("https://unit3d.tracker.org/announce/MySecretToken123456", "https://unit3d.tracker.org/announce/********")]
    [TestCase("udp://tracker.site:1337/0123456789abcdef0123456789abcdef/announce", "udp://tracker.site:1337/********/announce")]
    [TestCase("http://tracker.site/passkey/123456/announce", "http://tracker.site/passkey/********/announce")]
    [TestCase("http://tracker.site/authkey/abcdef/announce", "http://tracker.site/authkey/********/announce")]
    [TestCase("http://user:secret123456@tracker.site/announce", "http://user:********@tracker.site/announce")]
    public void Sanitize_WhenPathOrUserInfoHasPasskey_RedactsSecrets(string input, string expected)
    {
        TrackerUrlSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Test]
    [TestCase("http://private.tracker.org/announce?passkey=123456", "http://private.tracker.org/announce?passkey=********")]
    [TestCase("http://private.tracker.org/announce?authkey=123456", "http://private.tracker.org/announce?authkey=********")]
    [TestCase("http://private.tracker.org/announce?torrentpass=123456", "http://private.tracker.org/announce?torrentpass=********")]
    [TestCase("http://private.tracker.org/announce?torrent_pass=123456", "http://private.tracker.org/announce?torrent_pass=********")]
    [TestCase("http://private.tracker.org/announce?auth=abcdef123456", "http://private.tracker.org/announce?auth=********")]
    [TestCase("http://private.tracker.org/announce?token=abcdef123456789012", "http://private.tracker.org/announce?token=********")]
    [TestCase("http://private.tracker.org/announce?passkey=123456&peer_id=foo", "http://private.tracker.org/announce?passkey=********&peer_id=foo")]
    public void Sanitize_WhenQueryHasPasskey_RedactsQueryParameters(string input, string expected)
    {
        TrackerUrlSanitizer.Sanitize(input).Should().Be(expected);
    }
}
