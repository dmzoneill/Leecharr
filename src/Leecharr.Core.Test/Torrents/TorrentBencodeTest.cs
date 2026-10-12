// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentBencodeTest
{
    [Test]
    public void TryExtractInfoDictionaryBytes_ReturnsRawInfoSubstring()
    {
        var torrentBytes = "d8:announce35:http://tracker.example.com/announce4:infod4:name4:test6:lengthi1ee"u8.ToArray();

        var success = TorrentBencode.TryExtractInfoDictionaryBytes(torrentBytes, out var infoBytes);

        success.Should().BeTrue();
        infoBytes.ToArray().Should().Equal("d4:name4:test6:lengthi1ee"u8.ToArray());
    }
}
