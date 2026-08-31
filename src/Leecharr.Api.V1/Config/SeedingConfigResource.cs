using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class SeedingConfigResource : RestResource
{
    public int MaxUploadSpeedKbps { get; set; }
    public int MaxDownloadSpeedKbps { get; set; }
    public bool AlternativeSpeedEnabled { get; set; }
    public int AltUploadSpeedKbps { get; set; }
    public int AltDownloadSpeedKbps { get; set; }
    public double GlobalSeedRatioLimit { get; set; }
    public string UploadDistributionAlgorithm { get; set; }
    public int UploadDistributionSpreadPercentage { get; set; }
    public string UploadRedistributionMode { get; set; }
    public int UploadCustomIntervalMinutes { get; set; }
    public int UploadStoppedMinPercentage { get; set; }
    public int UploadStoppedMaxPercentage { get; set; }
    public string DownloadDistributionAlgorithm { get; set; }
    public int DownloadDistributionSpreadPercentage { get; set; }
    public string DownloadRedistributionMode { get; set; }
    public int DownloadCustomIntervalMinutes { get; set; }
    public int DownloadStoppedMinPercentage { get; set; }
    public int DownloadStoppedMaxPercentage { get; set; }
    public double SpeedVariationMin { get; set; }
    public double SpeedVariationMax { get; set; }
}

public static class SeedingConfigResourceMapper
{
    public static SeedingConfigResource ToResource(IConfigService model)
    {
        return new SeedingConfigResource
        {
            MaxUploadSpeedKbps = model.MaxUploadSpeedKbps,
            MaxDownloadSpeedKbps = model.MaxDownloadSpeedKbps,
            AlternativeSpeedEnabled = model.AlternativeSpeedEnabled,
            AltUploadSpeedKbps = model.AltUploadSpeedKbps,
            AltDownloadSpeedKbps = model.AltDownloadSpeedKbps,
            GlobalSeedRatioLimit = model.GlobalSeedRatioLimit,
            UploadDistributionAlgorithm = model.UploadDistributionAlgorithm,
            UploadDistributionSpreadPercentage = model.UploadDistributionSpreadPercentage,
            UploadRedistributionMode = model.UploadRedistributionMode,
            UploadCustomIntervalMinutes = model.UploadCustomIntervalMinutes,
            UploadStoppedMinPercentage = model.UploadStoppedMinPercentage,
            UploadStoppedMaxPercentage = model.UploadStoppedMaxPercentage,
            DownloadDistributionAlgorithm = model.DownloadDistributionAlgorithm,
            DownloadDistributionSpreadPercentage = model.DownloadDistributionSpreadPercentage,
            DownloadRedistributionMode = model.DownloadRedistributionMode,
            DownloadCustomIntervalMinutes = model.DownloadCustomIntervalMinutes,
            DownloadStoppedMinPercentage = model.DownloadStoppedMinPercentage,
            DownloadStoppedMaxPercentage = model.DownloadStoppedMaxPercentage,
            SpeedVariationMin = model.SpeedVariationMin,
            SpeedVariationMax = model.SpeedVariationMax
        };
    }
}
