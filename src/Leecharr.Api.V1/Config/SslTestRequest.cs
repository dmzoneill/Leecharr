// Copyright (c) PlaceholderCompany. All rights reserved.

namespace Leecharr.Api.V1.Config;

public class SslTestRequest
{
    public bool EnableSsl { get; set; } = true;

    public int SslPort { get; set; } = 7890;

    public string SslCertPath { get; set; } = string.Empty;

    public string SslKeyPath { get; set; } = string.Empty;

    public string SslCertPassword { get; set; } = string.Empty;

    public string BindAddress { get; set; } = "*";
}
