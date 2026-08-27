using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Ai;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

[V1ApiController("config/general")]
public class GeneralConfigController : ConfigController<GeneralConfigResource>
{
    private readonly IConfigFileProvider _configFileProvider;

    public GeneralConfigController(IConfigService configService, IConfigFileProvider configFileProvider)
        : base(configService)
    {
        _configFileProvider = configFileProvider;

        SharedValidator.RuleFor(c => c.WatchFolderScanIntervalSeconds)
            .GreaterThanOrEqualTo(1);
    }

    public override ActionResult<GeneralConfigResource> SaveConfig([FromBody] GeneralConfigResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        if (resource.ApiKey != null && resource.ApiKey.Contains('*'))
        {
            resource.ApiKey = _configFileProvider.ApiKey;
        }

        var fileUpdates = new Dictionary<string, object>
        {
            ["Port"] = resource.Port,
            ["BindAddress"] = resource.BindAddress,
            ["UrlBase"] = resource.UrlBase,
            ["AuthenticationEnabled"] = resource.AuthenticationEnabled,
            ["ApiKey"] = resource.ApiKey
        };

        _configFileProvider.SaveConfigDictionary(fileUpdates);

        return base.SaveConfig(resource);
    }

    protected override GeneralConfigResource ToResource(IConfigService model)
    {
        return GeneralConfigResourceMapper.ToResource(model, _configFileProvider);
    }
}

[V1ApiController("config/seeding")]
public class SeedingConfigController : ConfigController<SeedingConfigResource>
{
    public SeedingConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.MaxUploadSpeedKbps)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaxDownloadSpeedKbps)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.AltUploadSpeedKbps)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.AltDownloadSpeedKbps)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.GlobalSeedRatioLimit)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.UploadDistributionSpreadPercentage)
            .InclusiveBetween(0, 100);

        SharedValidator.RuleFor(c => c.DownloadDistributionSpreadPercentage)
            .InclusiveBetween(0, 100);
    }

    protected override SeedingConfigResource ToResource(IConfigService model)
    {
        return SeedingConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/network")]
public class NetworkConfigController : ConfigController<NetworkConfigResource>
{
    public NetworkConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.ListeningPort)
            .InclusiveBetween(1, 65535);

        SharedValidator.RuleFor(c => c.MaxGlobalConnections)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaxPerTorrentConnections)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaxUploadSlots)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.ProxyPort)
            .InclusiveBetween(1, 65535);
    }

    public override ActionResult<NetworkConfigResource> SaveConfig([FromBody] NetworkConfigResource resource)
    {
        if (resource.ProxyPassword == "********")
        {
            resource.ProxyPassword = _configService.ProxyPassword;
        }

        return base.SaveConfig(resource);
    }

    protected override NetworkConfigResource ToResource(IConfigService model)
    {
        return NetworkConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/bittorrent")]
public class BitTorrentConfigController : ConfigController<BitTorrentConfigResource>
{
    public BitTorrentConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.AnnounceIntervalSeconds)
            .GreaterThanOrEqualTo(60);

        SharedValidator.RuleFor(c => c.MinAnnounceIntervalSeconds)
            .GreaterThanOrEqualTo(30);

        SharedValidator.RuleFor(c => c.ScrapeIntervalSeconds)
            .GreaterThanOrEqualTo(60);
    }

    protected override BitTorrentConfigResource ToResource(IConfigService model)
    {
        return BitTorrentConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/peerprotocol")]
public class PeerProtocolConfigController : ConfigController<PeerProtocolConfigResource>
{
    public PeerProtocolConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.HandshakeTimeoutSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MessageReadTimeoutSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.KeepAliveIntervalSeconds)
            .GreaterThanOrEqualTo(30);

        SharedValidator.RuleFor(c => c.PeerRequestCount)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.SeederUploadActivityProbability)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.PeerIdleChance)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.PeerDropoutProbability)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.ConnectionRotationPercentage)
            .InclusiveBetween(0.0, 1.0);
    }

    protected override PeerProtocolConfigResource ToResource(IConfigService model)
    {
        return PeerProtocolConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/protocols")]
public class ProtocolsConfigController : ConfigController<ProtocolsConfigResource>
{
    public ProtocolsConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.TransportConnectionTimeoutSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.PexInterval)
            .GreaterThanOrEqualTo(10);

        SharedValidator.RuleFor(c => c.PexMaxPeersPerMessage)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.FailoverMaxConsecutiveFailures)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.DhtBucketSize)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.DhtMaxQueriesPerSecond)
            .GreaterThanOrEqualTo(1);
    }

    protected override ProtocolsConfigResource ToResource(IConfigService model)
    {
        return ProtocolsConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/simulation")]
public class SimulationConfigController : ConfigController<SimulationConfigResource>
{
    public SimulationConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.BehaviorVariation)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SwitchClientProbability)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SwarmAdaptationRate)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SwarmPeerAnalysisDepth)
            .GreaterThanOrEqualTo(1);
    }

    protected override SimulationConfigResource ToResource(IConfigService model)
    {
        return SimulationConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/trackerserver")]
public class TrackerServerConfigController : ConfigController<TrackerServerConfigResource>
{
    public TrackerServerConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.TrackerHttpPort)
            .InclusiveBetween(1, 65535);

        SharedValidator.RuleFor(c => c.TrackerUdpPort)
            .InclusiveBetween(1, 65535);

        SharedValidator.RuleFor(c => c.TrackerAnnounceInterval)
            .GreaterThanOrEqualTo(60);

        SharedValidator.RuleFor(c => c.TrackerMaxPeersPerAnnounce)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.TrackerRateLimitPerMinute)
            .GreaterThanOrEqualTo(1);
    }

    protected override TrackerServerConfigResource ToResource(IConfigService model)
    {
        return TrackerServerConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/scheduler")]
public class SchedulerConfigController : ConfigController<SchedulerConfigResource>
{
    public SchedulerConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.SchedulerStartHour)
            .InclusiveBetween(0, 23);

        SharedValidator.RuleFor(c => c.SchedulerStartMinute)
            .InclusiveBetween(0, 59);

        SharedValidator.RuleFor(c => c.SchedulerEndHour)
            .InclusiveBetween(0, 23);

        SharedValidator.RuleFor(c => c.SchedulerEndMinute)
            .InclusiveBetween(0, 59);
    }

    protected override SchedulerConfigResource ToResource(IConfigService model)
    {
        return SchedulerConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/advanced")]
public class AdvancedConfigController : ConfigController<AdvancedConfigResource>
{
    public AdvancedConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.UiRefreshRateSec)
            .GreaterThanOrEqualTo(1);
    }

    protected override AdvancedConfigResource ToResource(IConfigService model)
    {
        return AdvancedConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/ai")]
public class AiConfigController : ConfigController<AiConfigResource>
{
    private readonly IAiManager _aiManager;

    public AiConfigController(IConfigService configService, IAiManager aiManager = null)
        : base(configService)
    {
        _aiManager = aiManager;
    }

    public override ActionResult<AiConfigResource> SaveConfig([FromBody] AiConfigResource resource)
    {
        if (!string.IsNullOrEmpty(resource.GeminiApiKey) && resource.GeminiApiKey.Contains('*'))
        {
            resource.GeminiApiKey = _configService.GeminiApiKey;
        }

        var result = base.SaveConfig(resource);

        if (_aiManager != null && !string.IsNullOrWhiteSpace(resource.ActiveAiProvider) && !string.Equals(_aiManager.ActiveProviderId, resource.ActiveAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _aiManager.SwitchProviderAsync(resource.ActiveAiProvider);
                }
                catch
                {
                }
            });
        }

        return result;
    }

    protected override AiConfigResource ToResource(IConfigService model)
    {
        return AiConfigResourceMapper.ToResource(model);
    }
}
