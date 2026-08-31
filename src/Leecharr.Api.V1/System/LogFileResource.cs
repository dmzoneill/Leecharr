// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace Leecharr.Api.V1.System;

public class LogFileResource
{
    public string Filename { get; set; }

    public DateTime LastWriteTime { get; set; }

    public long Size { get; set; }
}
