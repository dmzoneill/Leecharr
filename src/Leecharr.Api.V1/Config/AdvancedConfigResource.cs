using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class AdvancedConfigResource : RestResource
{
    public bool LogToFile { get; set; }
    public string FileLogLevel { get; set; }
    public bool DebugMode { get; set; }
    public int UiRefreshRateSec { get; set; }
}

public static class AdvancedConfigResourceMapper
{
    public static AdvancedConfigResource ToResource(IConfigService model)
    {
        return new AdvancedConfigResource
        {
            LogToFile = model.LogToFile,
            FileLogLevel = model.FileLogLevel,
            DebugMode = model.DebugMode,
            UiRefreshRateSec = model.UiRefreshRateSec,
        };
    }
}
