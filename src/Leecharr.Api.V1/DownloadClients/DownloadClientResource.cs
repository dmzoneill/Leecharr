using Leecharr.Http.REST;
using System.Collections.Generic;

namespace Leecharr.Api.V1.DownloadClients;

public class DownloadClientResource : RestResource
{
    public string Name { get; set; }
    public string ClientType { get; set; }
    public string Host { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public bool UseSsl { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
}

public class DownloadClientTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
}

public class DownloadClientRemoteItem
{
    public string Id { get; set; }
    public string InfoHash { get; set; }
    public string Name { get; set; }
    public long Size { get; set; }
    public double Progress { get; set; }
    public string State { get; set; }
    public string SavePath { get; set; }
    public string Category { get; set; }
}

public class ImportRequest
{
    public List<string> Hashes { get; set; } = new();
    public string Category { get; set; }
    public string SavePath { get; set; }
    public bool StartPaused { get; set; }
}
