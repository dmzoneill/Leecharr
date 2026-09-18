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
                                      File.Exists("/run/.containerenv") ||
                                      File.Exists("/run/systemd/container") ||
                                      string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
                                      Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "1" ||
                                      !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("container")) ||
                                      !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")) ||
                                      CheckCgroups();

    public static string Os => RuntimeInformation.OSDescription;

    public static string Version => RuntimeInformation.FrameworkDescription;

    public static bool CheckCgroups()
    {
        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                var content = File.ReadAllText("/proc/1/cgroup");
                if (CheckCgroupContent(content))
                {
                    return true;
                }
            }

            if (File.Exists("/proc/self/cgroup"))
            {
                var content = File.ReadAllText("/proc/self/cgroup");
                if (CheckCgroupContent(content))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore access errors on restricted environments
        }

        return false;
    }

    public static bool CheckCgroupContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("kubepods", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("containerd", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("lxc", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("podman", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("libpod", StringComparison.OrdinalIgnoreCase);
    }
}
