// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace Leecharr.Api.V1.Network;

public class NetworkDiagnosticsResource
{
    public string LocalIp { get; set; } = "127.0.0.1";

    public string ExternalIp { get; set; } = string.Empty;

    public List<string> LocalAddresses { get; set; } = new();

    public bool UpnpAvailable { get; set; } = true;

    public bool ProxyEnabled { get; set; }

    public List<PortMappingResource> PortMappings { get; set; } = new();

    public int ListeningPort { get; set; } = 51413;

    public int ActiveConnections { get; set; }

    public int UploadSlots { get; set; } = 4;

    public bool DhtEnabled { get; set; } = true;

    public int DhtNodeCount { get; set; }

    public string EncryptionMode { get; set; } = "PreferEncrypted";

    public int EncryptedConnections { get; set; }

    public int PlaintextConnections { get; set; }

    public double EncryptionPercentage { get; set; } = 100.0;
}

public class PortMappingResource
{
    public int InternalPort { get; set; }

    public int ExternalPort { get; set; }

    public string Protocol { get; set; } = "TCP";

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
