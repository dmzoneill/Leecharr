// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Network.Binding;

public class NetworkBindingCapabilities
{
    public bool SupportsInterfaceBinding { get; set; }

    public bool SupportsSoBindToDevice { get; set; }

    public bool SupportsSocks5Proxy { get; set; }

    public bool SupportsTorOnion { get; set; }

    public bool SupportsVpnKillSwitch { get; set; }

    public bool SupportsAnonymousRouting { get; set; }
}
