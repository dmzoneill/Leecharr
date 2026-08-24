using System.Threading.Tasks;

namespace NzbDrone.Core.Network.GeoIp;

public interface IGeoIpProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    string Version { get; }
    bool IsAvailable { get; }
    GeoIpCapabilities Capabilities { get; }
    Task<GeoIpHealthResult> ProbeHealthAsync();
    Task<GeoLocationInfo> LookupAsync(string ipAddress);
}
