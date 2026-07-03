namespace NzbDrone.Common.EnvironmentInfo;

public interface IRuntimeInfo
{
    bool IsWindowsService { get; }
    bool RestartPending { get; set; }
}

public class RuntimeInfo : IRuntimeInfo
{
    public bool IsWindowsService => false;
    public bool RestartPending { get; set; }
}
