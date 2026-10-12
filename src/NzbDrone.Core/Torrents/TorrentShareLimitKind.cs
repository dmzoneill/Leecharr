// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Torrents;

public enum TorrentShareLimitKind
{
    Default = 0,
    Global = 1,
    Unlimited = 2,
    Explicit = 3,
}
