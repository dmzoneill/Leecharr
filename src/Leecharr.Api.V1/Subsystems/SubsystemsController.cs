using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;

namespace Leecharr.Api.V1.Subsystems;

[V1ApiController("subsystems")]
public class SubsystemsController : Controller
{
    private readonly ITorrentEngineManager _torrentEngineManager;
    private readonly IArchiveExtractorManager _extractorManager;
    private readonly IMediaInspectorManager _mediaInspectorManager;
    private readonly IGeoIpManager _geoIpManager;
    private readonly IBlocklistManager _blocklistManager;
    private readonly INetworkBindingManager _networkBindingManager;
    private readonly IMediaMetadataManager _mediaMetadataManager;
    private readonly IHttpTransportManager _httpTransportManager;
    private readonly IAiManager _aiManager;

    public SubsystemsController(
        ITorrentEngineManager torrentEngineManager,
        IArchiveExtractorManager extractorManager,
        IMediaInspectorManager mediaInspectorManager,
        IGeoIpManager geoIpManager,
        IBlocklistManager blocklistManager,
        INetworkBindingManager networkBindingManager,
        IMediaMetadataManager mediaMetadataManager,
        IHttpTransportManager httpTransportManager,
        IAiManager aiManager)
    {
        _torrentEngineManager = torrentEngineManager;
        _extractorManager = extractorManager;
        _mediaInspectorManager = mediaInspectorManager;
        _geoIpManager = geoIpManager;
        _blocklistManager = blocklistManager;
        _networkBindingManager = networkBindingManager;
        _mediaMetadataManager = mediaMetadataManager;
        _httpTransportManager = httpTransportManager;
        _aiManager = aiManager;
    }

    [HttpGet]
    public ActionResult<List<SubsystemOverviewResource>> GetAllSubsystems()
    {
        var result = new List<SubsystemOverviewResource>
        {
            BuildTorrentEngineSubsystem(),
            BuildExtractorSubsystem(),
            BuildMediaInspectorSubsystem(),
            BuildGeoIpSubsystem(),
            BuildBlocklistSubsystem(),
            BuildNetworkBindingSubsystem(),
            BuildMediaMetadataSubsystem(),
            BuildHttpTransportSubsystem(),
            BuildAiSubsystem()
        };

        return Ok(result);
    }

    [HttpGet("{subsystemId}")]
    public ActionResult<SubsystemOverviewResource> GetSubsystem(string subsystemId)
    {
        var normalized = subsystemId?.ToLowerInvariant();
        var subsystem = normalized switch
        {
            "bittorrent" or "torrentengine" => BuildTorrentEngineSubsystem(),
            "extractor" or "archiveextractor" => BuildExtractorSubsystem(),
            "mediainspector" or "inspector" => BuildMediaInspectorSubsystem(),
            "geoip" => BuildGeoIpSubsystem(),
            "blocklist" => BuildBlocklistSubsystem(),
            "networkbinding" or "binding" => BuildNetworkBindingSubsystem(),
            "mediametadata" or "metadata" => BuildMediaMetadataSubsystem(),
            "httptransport" or "transport" => BuildHttpTransportSubsystem(),
            "ai" or "intelligence" => BuildAiSubsystem(),
            _ => null
        };

        if (subsystem == null)
        {
            return NotFound(new { error = $"Subsystem '{subsystemId}' not found." });
        }

        return Ok(subsystem);
    }

    [HttpPost("{subsystemId}/switch")]
    public async Task<ActionResult<SwitchSubsystemProviderResult>> SwitchProvider(string subsystemId, [FromBody] SwitchSubsystemProviderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return BadRequest(new SwitchSubsystemProviderResult
            {
                Success = false,
                SubsystemId = subsystemId,
                Error = "ProviderId is required."
            });
        }

        var normalized = subsystemId?.ToLowerInvariant();
        switch (normalized)
        {
            case "bittorrent" or "torrentengine":
                var torrentRes = await _torrentEngineManager.SwitchEngineAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = torrentRes.Success,
                    SubsystemId = "bittorrent",
                    PreviousProvider = torrentRes.PreviousEngine,
                    ActiveProvider = torrentRes.ActiveEngine,
                    Message = torrentRes.Message,
                    Error = torrentRes.Error
                });

            case "extractor" or "archiveextractor":
                var extractRes = await _extractorManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = extractRes.Success,
                    SubsystemId = "extractor",
                    PreviousProvider = extractRes.PreviousProvider,
                    ActiveProvider = extractRes.ActiveProvider,
                    Message = extractRes.Message,
                    Error = extractRes.Error
                });

            case "mediainspector" or "inspector":
                var mediaRes = await _mediaInspectorManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = mediaRes.Success,
                    SubsystemId = "mediainspector",
                    PreviousProvider = mediaRes.PreviousProvider,
                    ActiveProvider = mediaRes.ActiveProvider,
                    Message = mediaRes.Message,
                    Error = mediaRes.Error
                });

            case "geoip":
                var previousGeo = _geoIpManager.ActiveProviderId;
                var geoSuccess = await _geoIpManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = geoSuccess,
                    SubsystemId = "geoip",
                    PreviousProvider = previousGeo,
                    ActiveProvider = _geoIpManager.ActiveProviderId,
                    Message = geoSuccess ? $"Switched GeoIP provider to {request.ProviderId}." : $"Failed to switch GeoIP provider to {request.ProviderId}."
                });

            case "blocklist":
                var previousBlock = _blocklistManager.ActiveProviderId;
                var blockSuccess = await _blocklistManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = blockSuccess,
                    SubsystemId = "blocklist",
                    PreviousProvider = previousBlock,
                    ActiveProvider = _blocklistManager.ActiveProviderId,
                    Message = blockSuccess ? $"Switched Blocklist provider to {request.ProviderId}." : $"Failed to switch Blocklist provider to {request.ProviderId}."
                });

            case "networkbinding" or "binding":
                var netRes = await _networkBindingManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = netRes.Success,
                    SubsystemId = "networkbinding",
                    PreviousProvider = netRes.PreviousProvider,
                    ActiveProvider = netRes.ActiveProvider,
                    Message = netRes.Message,
                    Error = netRes.Error
                });

            case "mediametadata" or "metadata":
                var metaRes = await _mediaMetadataManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = metaRes.Success,
                    SubsystemId = "mediametadata",
                    PreviousProvider = metaRes.PreviousProvider,
                    ActiveProvider = metaRes.ActiveProvider,
                    Message = metaRes.Message,
                    Error = metaRes.Error
                });

            case "httptransport" or "transport":
                var httpRes = await _httpTransportManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = httpRes.Success,
                    SubsystemId = "httptransport",
                    PreviousProvider = httpRes.PreviousProvider,
                    ActiveProvider = httpRes.ActiveProvider,
                    Message = httpRes.Message,
                    Error = httpRes.Error
                });

            case "ai" or "intelligence":
                var previousAi = _aiManager.ActiveProviderId;
                var aiSuccess = await _aiManager.SwitchProviderAsync(request.ProviderId);
                return Ok(new SwitchSubsystemProviderResult
                {
                    Success = aiSuccess,
                    SubsystemId = "ai",
                    PreviousProvider = previousAi,
                    ActiveProvider = _aiManager.ActiveProviderId,
                    Message = aiSuccess ? $"Switched AI provider to {request.ProviderId}." : $"Failed to switch AI provider to {request.ProviderId}."
                });

            default:
                return NotFound(new SwitchSubsystemProviderResult
                {
                    Success = false,
                    SubsystemId = subsystemId,
                    Error = $"Subsystem '{subsystemId}' is unknown."
                });
        }
    }

    [HttpPost("{subsystemId}/probe/{providerId}")]
    public async Task<ActionResult<SubsystemProbeResult>> ProbeProvider(string subsystemId, string providerId)
    {
        var normalized = subsystemId?.ToLowerInvariant();
        switch (normalized)
        {
            case "bittorrent" or "torrentengine":
                var torrentProbe = await _torrentEngineManager.ProbeEngineAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "bittorrent",
                    ProviderId = providerId,
                    IsHealthy = torrentProbe.IsHealthy,
                    StatusMessage = torrentProbe.StatusMessage,
                    DependencyChecks = torrentProbe.DependencyChecks,
                    Warnings = torrentProbe.Warnings
                });

            case "extractor" or "archiveextractor":
                var extractProbe = await _extractorManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "extractor",
                    ProviderId = providerId,
                    IsHealthy = extractProbe.IsHealthy,
                    StatusMessage = extractProbe.StatusMessage,
                    DependencyChecks = extractProbe.DependencyChecks,
                    Warnings = extractProbe.Warnings
                });

            case "mediainspector" or "inspector":
                var mediaProbe = await _mediaInspectorManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "mediainspector",
                    ProviderId = providerId,
                    IsHealthy = mediaProbe.IsHealthy,
                    StatusMessage = mediaProbe.StatusMessage,
                    DependencyChecks = mediaProbe.DependencyChecks,
                    Warnings = mediaProbe.Warnings
                });

            case "geoip":
                var geoProbe = await _geoIpManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "geoip",
                    ProviderId = providerId,
                    IsHealthy = geoProbe.IsHealthy,
                    StatusMessage = geoProbe.StatusMessage,
                    Warnings = geoProbe.Warnings
                });

            case "blocklist":
                var blockProbe = await _blocklistManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "blocklist",
                    ProviderId = providerId,
                    IsHealthy = blockProbe.IsHealthy,
                    StatusMessage = blockProbe.StatusMessage,
                    Warnings = blockProbe.Warnings
                });

            case "networkbinding" or "binding":
                var netProbe = await _networkBindingManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "networkbinding",
                    ProviderId = providerId,
                    IsHealthy = netProbe.IsHealthy,
                    StatusMessage = netProbe.StatusMessage,
                    Warnings = netProbe.Warnings
                });

            case "mediametadata" or "metadata":
                var metaProbe = await _mediaMetadataManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "mediametadata",
                    ProviderId = providerId,
                    IsHealthy = metaProbe.IsHealthy,
                    StatusMessage = metaProbe.StatusMessage,
                    Warnings = metaProbe.Warnings
                });

            case "httptransport" or "transport":
                var httpProbe = await _httpTransportManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "httptransport",
                    ProviderId = providerId,
                    IsHealthy = httpProbe.IsHealthy,
                    StatusMessage = httpProbe.StatusMessage,
                    Warnings = httpProbe.Warnings
                });

            case "ai" or "intelligence":
                var aiProbe = await _aiManager.ProbeProviderAsync(providerId);
                return Ok(new SubsystemProbeResult
                {
                    SubsystemId = "ai",
                    ProviderId = providerId,
                    IsHealthy = aiProbe.IsHealthy,
                    StatusMessage = aiProbe.StatusMessage,
                    Warnings = aiProbe.Warnings
                });

            default:
                return NotFound(new { error = $"Subsystem '{subsystemId}' is unknown." });
        }
    }

    private SubsystemOverviewResource BuildTorrentEngineSubsystem()
    {
        var activeId = _torrentEngineManager.ActiveEngineId;
        return new SubsystemOverviewResource
        {
            Id = "bittorrent",
            Name = "BitTorrent Engine",
            Category = "Core Download Engine",
            Description = "Primary BitTorrent downloader core managing swarm sessions, piece picking, and disk I/O.",
            ActiveProviderId = activeId,
            Providers = _torrentEngineManager.GetEngines().Select(e => new SubsystemProviderResource
            {
                ProviderId = e.EngineId,
                DisplayName = e.DisplayName,
                Version = e.Version,
                Description = e.Description,
                IsActive = string.Equals(e.EngineId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = e.IsAvailable,
                Status = string.Equals(e.EngineId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (e.IsAvailable ? "Ready" : "Emulated"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsSequentialDownload"] = e.Capabilities.SupportsSequentialDownload,
                    ["supportsSparseAllocation"] = e.Capabilities.SupportsSparseAllocation,
                    ["supportsV2Torrents"] = e.Capabilities.SupportsV2Torrents,
                    ["supportsUtp"] = e.Capabilities.SupportsUtp,
                    ["supportsDht"] = e.Capabilities.SupportsDht,
                    ["supportsMemoryMappedIo"] = e.Capabilities.SupportsMemoryMappedIo
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildExtractorSubsystem()
    {
        var activeId = _extractorManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "extractor",
            Name = "Archive Extractor",
            Category = "Post-Processing Pipeline",
            Description = "Unpacks multi-part RAR, 7z, and ZIP archives upon download completion.",
            ActiveProviderId = activeId,
            Providers = _extractorManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = p.Description,
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Not Found"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsRar5"] = p.Capabilities.SupportsRar5,
                    ["supports7z"] = p.Capabilities.Supports7z,
                    ["supportsMultiPart"] = p.Capabilities.SupportsMultiPart,
                    ["supportsPasswordProtected"] = p.Capabilities.SupportsPasswordProtected
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildMediaInspectorSubsystem()
    {
        var activeId = _mediaInspectorManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "mediainspector",
            Name = "Media Container & Stream Inspector",
            Category = "Media Intelligence",
            Description = "Extracts codecs, HDR formats (Dolby Vision/HDR10+), audio tracks, and container specs.",
            ActiveProviderId = activeId,
            Providers = _mediaInspectorManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = p.Description,
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Not Found"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsDolbyVision"] = p.Capabilities.SupportsDolbyVision,
                    ["supportsHdr10Plus"] = p.Capabilities.SupportsHdr10Plus,
                    ["supportsEac3Atmos"] = p.Capabilities.SupportsEac3Atmos,
                    ["supportsSubtitleTracks"] = p.Capabilities.SupportsSubtitleTracks,
                    ["supportsPureManagedStreams"] = p.Capabilities.SupportsPureManagedStreams
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildGeoIpSubsystem()
    {
        var activeId = _geoIpManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "geoip",
            Name = "Swarm GeoIP Geolocation",
            Category = "Swarm Intelligence",
            Description = "Resolves peer IP addresses into countries, cities, and ISP badges for the peer map visualizer.",
            ActiveProviderId = activeId,
            Providers = _geoIpManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = $"{p.DisplayName} geolocation resolver",
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Not Found"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsCountry"] = p.Capabilities.HasFlag(GeoIpCapabilities.Country),
                    ["supportsCity"] = p.Capabilities.HasFlag(GeoIpCapabilities.City),
                    ["supportsAsn"] = p.Capabilities.HasFlag(GeoIpCapabilities.Asn),
                    ["supportsOfflineDatabase"] = p.Capabilities.HasFlag(GeoIpCapabilities.OfflineDatabase)
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildBlocklistSubsystem()
    {
        var activeId = _blocklistManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "blocklist",
            Name = "IP Blocklist & Threat Intelligence Filter",
            Category = "Network & Security",
            Description = "Filters malicious peer IP addresses, ranges, and CIDR subnets before establishing connections.",
            ActiveProviderId = activeId,
            Providers = _blocklistManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = $"{p.DisplayName} IP filter engine",
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Not Found"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsIPv4"] = p.Capabilities.HasFlag(BlocklistCapabilities.IPv4),
                    ["supportsIPv6"] = p.Capabilities.HasFlag(BlocklistCapabilities.IPv6),
                    ["supportsCidr"] = p.Capabilities.HasFlag(BlocklistCapabilities.Cidr),
                    ["supportsLinuxIpSet"] = p.Capabilities.HasFlag(BlocklistCapabilities.LinuxIpSet)
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildNetworkBindingSubsystem()
    {
        var activeId = _networkBindingManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "networkbinding",
            Name = "Network Interface Binding & VPN Kill Switch",
            Category = "Network & Security",
            Description = "Enforces socket routing through specific VPN interfaces (tun0/wg0) with zero traffic leaks.",
            ActiveProviderId = activeId,
            Providers = _networkBindingManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = $"{p.DisplayName} binding adapter",
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Not Supported"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsInterfaceBinding"] = p.Capabilities.SupportsInterfaceBinding,
                    ["supportsKernelLock"] = p.Capabilities.SupportsSoBindToDevice,
                    ["supportsProxyTunnel"] = p.Capabilities.SupportsSocks5Proxy,
                    ["supportsVpnKillSwitch"] = p.Capabilities.SupportsVpnKillSwitch
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildMediaMetadataSubsystem()
    {
        var activeId = _mediaMetadataManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "mediametadata",
            Name = "Media Enrichment & Library Metadata",
            Category = "Media Intelligence",
            Description = "Fetches rich posters, backdrops, season banners, ratings, and cast descriptions for media downloads.",
            ActiveProviderId = activeId,
            Providers = _mediaMetadataManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = p.Description,
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Disabled"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsMovies"] = p.Capabilities.SupportsMovies,
                    ["supportsTvSeries"] = p.Capabilities.SupportsTvSeries,
                    ["supportsMusic"] = p.Capabilities.SupportsMusic,
                    ["supportsPosters"] = p.Capabilities.SupportsPosters,
                    ["supportsFanart"] = p.Capabilities.SupportsFanart
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildHttpTransportSubsystem()
    {
        var activeId = _httpTransportManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "httptransport",
            Name = "HTTP Transport & Anti-Bot Engine",
            Category = "Network & Security",
            Description = "Handles tracker announces, RSS sync, and Torznab indexer queries with TLS fingerprinting and challenge solving.",
            ActiveProviderId = activeId,
            Providers = _httpTransportManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = p.Description,
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Not Found"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsHttp3Quic"] = p.Capabilities.SupportsHttp3Quic,
                    ["supportsBrowserFingerprintEmulation"] = p.Capabilities.SupportsBrowserFingerprintEmulation,
                    ["supportsFlareSolverr"] = p.Capabilities.SupportsFlareSolverr,
                    ["supportsTlsJa3Ja4Fingerprinting"] = p.Capabilities.SupportsTlsJa3Ja4Fingerprinting
                }
            }).ToList()
        };
    }

    private SubsystemOverviewResource BuildAiSubsystem()
    {
        var activeId = _aiManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "ai",
            Name = "Artificial Intelligence & Copilot Engine",
            Category = "AI & Smart Automation",
            Description = "Provides natural language search, intelligent scene release de-obfuscation, automated swarm health diagnostics, malware anomaly detection, and conversational Copilot assistance.",
            ActiveProviderId = activeId,
            Providers = _aiManager.GetProviders().Select(p => new SubsystemProviderResource
            {
                ProviderId = p.ProviderId,
                DisplayName = p.DisplayName,
                Version = p.Version,
                Description = p.Description,
                IsActive = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase),
                IsAvailable = p.IsAvailable,
                Status = string.Equals(p.ProviderId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (p.IsAvailable ? "Ready" : "Disabled"),
                Capabilities = new Dictionary<string, object>
                {
                    ["supportsNaturalLanguageSearch"] = p.Capabilities.HasFlag(AiCapabilities.SupportsNaturalLanguageSearch),
                    ["supportsReleaseNameParsing"] = p.Capabilities.HasFlag(AiCapabilities.SupportsReleaseNameParsing),
                    ["supportsDiagnosticCopilot"] = p.Capabilities.HasFlag(AiCapabilities.SupportsDiagnosticCopilot),
                    ["supportsMalwareAnomalyDetection"] = p.Capabilities.HasFlag(AiCapabilities.SupportsMalwareAnomalyDetection),
                    ["supportsSwarmOptimization"] = p.Capabilities.HasFlag(AiCapabilities.SupportsSwarmOptimization),
                    ["supportsLocalOfflineInference"] = p.Capabilities.HasFlag(AiCapabilities.SupportsLocalOfflineInference),
                    ["supportsCloudLlm"] = p.Capabilities.HasFlag(AiCapabilities.SupportsCloudLlm)
                }
            }).ToList()
        };
    }
}
