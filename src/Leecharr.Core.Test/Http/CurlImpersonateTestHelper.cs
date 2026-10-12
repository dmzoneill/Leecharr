// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace Leecharr.Core.Test.Http;

internal static class CurlImpersonateTestHelper
{
    public static CurlImpersonateTransportProvider CreateProvider(
        IConfigService configService = null,
        INetworkBindingService networkBindingService = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        return new CurlImpersonateTransportProvider(
            configService,
            networkBindingService,
            vpnKillSwitchService,
            CreateSystemCurlShimBinary());
    }

    public static string CreateSystemCurlShimBinary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "leecharr-curl-shim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var binaryPath = Path.Combine(dir, "curl_chrome116");

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(binaryPath, "@echo off\r\ncurl %*\r\n");
        }
        else
        {
            File.WriteAllText(binaryPath, "#!/bin/sh\nexec /usr/bin/curl \"$@\"\n");
            using var chmod = Process.Start(new ProcessStartInfo("chmod", $"+x {binaryPath}")
            {
                UseShellExecute = false,
            });
            chmod?.WaitForExit();
        }

        return binaryPath;
    }
}
