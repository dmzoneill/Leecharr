// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Ai;

public class AiSwitchResult
{
    public bool Success { get; set; }

    public string PreviousProviderId { get; set; }

    public string ActiveProviderId { get; set; }

    public string Message { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
