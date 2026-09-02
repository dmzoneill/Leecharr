// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Network;

public class NetworkSettings : ModelBase
{
    public string BindInterface { get; set; }

    public bool EnableVpnKillSwitch { get; set; }

    public bool EnableUpnp { get; set; } = true;

    public bool EnableNatPmp { get; set; } = true;

    public int ListenPort { get; set; } = 51413;

    public bool RandomizePortOnLaunch { get; set; }

    public bool EnableProxy { get; set; }

    public string ProxyType { get; set; } = "SOCKS5";

    public string ProxyHost { get; set; }

    public int ProxyPort { get; set; } = 1080;

    public string ProxyUsername { get; set; }

    public string ProxyPassword { get; set; }

    public bool ProxyPeers { get; set; } = true;

    public bool ProxyTrackers { get; set; } = true;

    public bool ProxyIndexers { get; set; } = true;

    public bool AnonymousMode { get; set; }

    public string ClientEmulationPreset { get; set; } = "Leecharr";
}
