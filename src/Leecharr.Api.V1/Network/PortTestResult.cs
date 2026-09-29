// Copyright (c) PlaceholderCompany. All rights reserved.

namespace Leecharr.Api.V1.Network;

public class PortTestRequest
{
    public int? Port { get; set; }
}

public class PortTestResult
{
    public int Port { get; set; }

    public bool IsOpen { get; set; }

    public string Message { get; set; } = string.Empty;
}
