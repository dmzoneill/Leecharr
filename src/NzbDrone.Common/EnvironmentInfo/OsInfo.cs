using System.Runtime.InteropServices;

namespace NzbDrone.Common.EnvironmentInfo;

public static class OsInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsOsx => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static string Os => RuntimeInformation.OSDescription;
    public static string Version => RuntimeInformation.FrameworkDescription;
}
