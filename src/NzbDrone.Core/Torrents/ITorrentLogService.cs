// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface ITorrentLogService
{
    void Log(int torrentId, string level, string source, string message, DateTime? timestamp = null);

    IReadOnlyList<TorrentEventLog> GetLogs(int torrentId, int limit = 100);

    void ClearLogs(int torrentId);

    void ClearAll();
}
