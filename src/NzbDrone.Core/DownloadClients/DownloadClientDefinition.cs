// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientDefinition : ModelBase
{
    public string Name { get; set; }

    public string ClientType { get; set; }

    public string Host { get; set; }

    public int Port { get; set; } = 8080;

    public bool UseSsl { get; set; }

    public string Username { get; set; }

    public string Password { get; set; }

    public string Category { get; set; }

    public bool Enable { get; set; } = true;

    public int Priority { get; set; } = 1;
}
