// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Peers;

public interface IPeerConnectionHistoryService
{
    void RecordEvent(PeerConnectionEvent connectionEvent);

    IReadOnlyList<PeerConnectionEvent> GetRecords(DateTime? start = null, DateTime? end = null, string infoHash = null);

    void Purge(DateTime? before = null);

    void Clear();
}
