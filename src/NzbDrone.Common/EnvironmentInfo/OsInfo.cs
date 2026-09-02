// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NzbDrone.Common.EnvironmentInfo;

public static class OsInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsOsx => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsDocker => File.Exists("/.dockerenv") || IsContainer;

    public static bool IsContainer => File.Exists("/.dockerenv") ||
                                      string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
                                      Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "1";

    public static string Os => RuntimeInformation.OSDescription;

    public static string Version => RuntimeInformation.FrameworkDescription;
}
