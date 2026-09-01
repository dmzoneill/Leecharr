using System.Threading.Tasks;

namespace NzbDrone.Core.Network.GeoIp;

public interface IGeoIpService
{
    Task<GeoLocationInfo> LookupAsync(string ipAddress);
    GeoLocationInfo Lookup(string ipAddress);
}
