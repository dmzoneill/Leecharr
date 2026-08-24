using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class NetworkConfigResource : RestResource
{
    public int ListeningPort { get; set; }
    public bool UpnpEnabled { get; set; }
    public int MaxGlobalConnections { get; set; }
    public int MaxPerTorrentConnections { get; set; }
    public int MaxUploadSlots { get; set; }
    public string ProxyType { get; set; }
    public string ProxyHost { get; set; }
    public int ProxyPort { get; set; }
    public bool ProxyAuthEnabled { get; set; }
    public string ProxyUsername { get; set; }
    public string ProxyPassword { get; set; }
}

public static class NetworkConfigResourceMapper
{
    public static NetworkConfigResource ToResource(IConfigService model)
    {
        return new NetworkConfigResource
        {
            ListeningPort = model.ListeningPort,
            UpnpEnabled = model.UpnpEnabled,
            MaxGlobalConnections = model.MaxGlobalConnections,
            MaxPerTorrentConnections = model.MaxPerTorrentConnections,
            MaxUploadSlots = model.MaxUploadSlots,
            ProxyType = model.ProxyType,
            ProxyHost = model.ProxyHost,
            ProxyPort = model.ProxyPort,
            ProxyAuthEnabled = model.ProxyAuthEnabled,
            ProxyUsername = model.ProxyUsername,
            ProxyPassword = string.IsNullOrEmpty(model.ProxyPassword) ? string.Empty : "********"
        };
    }
}
