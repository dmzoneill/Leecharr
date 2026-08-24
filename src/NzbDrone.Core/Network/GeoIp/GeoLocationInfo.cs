namespace NzbDrone.Core.Network.GeoIp;

public class GeoLocationInfo
{
    public string IpAddress { get; set; }
    public string CountryCode { get; set; }
    public string CountryName { get; set; }
    public string City { get; set; }
    public string Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Asn { get; set; }
    public string Isp { get; set; }
    public string TimeZone { get; set; }
}
