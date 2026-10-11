// Copyright (c) FeedItOut. All rights reserved.

using MonoTorrent.BEncoding;

namespace NzbDrone.Core.BitTorrent.Tracker;

public static class TrackerProtocolEncoding
{
    public static byte[] BuildFailure(string reason)
    {
        var dict = new BEncodedDictionary
        {
            { "failure reason", new BEncodedString(reason) },
        };
        return dict.Encode();
    }
}
