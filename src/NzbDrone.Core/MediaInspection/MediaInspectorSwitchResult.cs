// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.MediaInspection;

public class MediaInspectorSwitchResult
{
    public bool Success { get; set; }

    public string PreviousProvider { get; set; }

    public string ActiveProvider { get; set; }

    public string Message { get; set; }

    public string Error { get; set; }
}
