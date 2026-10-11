// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Trackers;

[TestFixture]
public class TrackerAnnounceUrlCanonicalizerTest
{
    [Test]
    [TestCase("http://tracker.example/announce", "http://tracker.example/announce")]
    [TestCase("http://tracker.example/announce/", "http://tracker.example/announce")]
    [TestCase("http://tracker.example:80/announce", "http://tracker.example/announce")]
    [TestCase("https://tracker.tamersunion.org:443/announce", "https://tracker.tamersunion.org/announce")]
    [TestCase("https://tracker.tamersunion.org/announce", "https://tracker.tamersunion.org/announce")]
    public void Canonicalize_NormalizesEquivalentAnnounceUrls(string input, string expected)
    {
        TrackerAnnounceUrlCanonicalizer.Canonicalize(input).Should().Be(expected);
    }

    [Test]
    public void AreEquivalent_TreatsTrailingSlashAndDefaultPortAsSame()
    {
        TrackerAnnounceUrlCanonicalizer.AreEquivalent(
            "http://tracker.example/announce",
            "http://tracker.example/announce/").Should().BeTrue();

        TrackerAnnounceUrlCanonicalizer.AreEquivalent(
            "http://tracker.example:80/announce",
            "http://tracker.example/announce").Should().BeTrue();
    }

    [Test]
    public void AreEquivalent_PreservesDistinctTrackers()
    {
        TrackerAnnounceUrlCanonicalizer.AreEquivalent(
            "http://tracker.example/announce",
            "http://other.example/announce").Should().BeFalse();
    }
}
