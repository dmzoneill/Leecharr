// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Common.Instrumentation;

public class LogEntryRecord
{
    public long Id { get; set; }

    public DateTime Time { get; set; }

    public string Level { get; set; }

    public string Logger { get; set; }

    public string Message { get; set; }

    public string Exception { get; set; }
}
