using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class BitTorrentConfigResource : RestResource
{
    public bool EnableDht { get; set; }
    public bool EnablePex { get; set; }
    public bool EnableLpd { get; set; }
    public string EncryptionMode { get; set; }
    public string BitTorrentUserAgent { get; set; }
    public string PeerIdPrefix { get; set; }
    public int AnnounceIntervalSeconds { get; set; }
    public int MinAnnounceIntervalSeconds { get; set; }
    public int ScrapeIntervalSeconds { get; set; }
}

public static class BitTorrentConfigResourceMapper
{
    public static BitTorrentConfigResource ToResource(IConfigService model)
    {
        return new BitTorrentConfigResource
        {
            EnableDht = model.EnableDht,
            EnablePex = model.EnablePex,
            EnableLpd = model.EnableLpd,
            EncryptionMode = model.EncryptionMode,
            BitTorrentUserAgent = model.BitTorrentUserAgent,
            PeerIdPrefix = model.PeerIdPrefix,
            AnnounceIntervalSeconds = model.AnnounceIntervalSeconds,
            MinAnnounceIntervalSeconds = model.MinAnnounceIntervalSeconds,
            ScrapeIntervalSeconds = model.ScrapeIntervalSeconds
        };
    }
}
