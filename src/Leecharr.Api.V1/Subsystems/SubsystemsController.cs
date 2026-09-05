// Copyright (c) PlaceholderCompany. All rights reserved.

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
using NzbDrone.Core.Telemetry;

namespace Leecharr.Api.V1.Subsystems;

[V1ApiController("subsystems")]
public class SubsystemsController : Controller
{
    private readonly ITorrentEngineManager torrentEngineManager;
    private readonly IArchiveExtractorManager extractorManager;
    private readonly IMediaInspectorManager mediaInspectorManager;
    private readonly IGeoIpManager geoIpManager;
    private readonly IBlocklistManager blocklistManager;
    private readonly INetworkBindingManager networkBindingManager;
    private readonly IMediaMetadataManager mediaMetadataManager;
    private readonly IHttpTransportManager httpTransportManager;
    private readonly IAiManager aiManager;
    private readonly ISystemResourceService resourceService;
    private readonly IBlocklistUpdateService blocklistUpdateService;

    public SubsystemsController(
        ITorrentEngineManager torrentEngineManager,
        IArchiveExtractorManager extractorManager,
        IMediaInspectorManager mediaInspectorManager,
        IGeoIpManager geoIpManager,
        IBlocklistManager blocklistManager,
        INetworkBindingManager networkBindingManager,
        IMediaMetadataManager mediaMetadataManager,
        IHttpTransportManager httpTransportManager,
        IAiManager aiManager,
        ISystemResourceService resourceService,
        IBlocklistUpdateService blocklistUpdateService = null)
    {
        this.torrentEngineManager = torrentEngineManager;
        this.extractorManager = extractorManager;
        this.mediaInspectorManager = mediaInspectorManager;
        this.geoIpManager = geoIpManager;
        this.blocklistManager = blocklistManager;
        this.networkBindingManager = networkBindingManager;
        this.mediaMetadataManager = mediaMetadataManager;
        this.httpTransportManager = httpTransportManager;
        this.aiManager = aiManager;
        this.resourceService = resourceService;
        this.blocklistUpdateService = blocklistUpdateService;
    }

    [HttpPost("blocklist/update")]
    public async Task<IActionResult> UpdateBlocklistRules()
    {
        var loaded = this.blocklistUpdateService != null
            ? await this.blocklistUpdateService.UpdateRulesAsync()
            : 0;

        return this.Ok(new
        {
            success = true,
            rulesLoaded = loaded,
            activeRules = this.blocklistManager.ActiveProvider?.RuleCount ?? 0,
            activeProvider = this.blocklistManager.ActiveProviderId,
        });
    }

    [HttpGet("metrics")]
    public ActionResult<List<SubsystemTelemetryReport>> GetSubsystemsMetrics()
    {
        return this.Ok(this.resourceService.GetSubsystemTelemetry());
    }

    [HttpGet("{subsystemId}/metrics")]
    public ActionResult<SubsystemTelemetryReport> GetSubsystemMetrics(string subsystemId)
    {
        var normalized = subsystemId?.ToLowerInvariant();
        var telemetry = this.resourceService.GetSubsystemTelemetry()
            .FirstOrDefault(t => string.Equals(t.SubsystemId, normalized, StringComparison.OrdinalIgnoreCase));

        if (telemetry == null)
        {
            return this.NotFound(new { error = $"Telemetry report for subsystem '{subsystemId}' was not found." });
        }

        return this.Ok(telemetry);
    }

    [HttpGet]
    public ActionResult<List<SubsystemOverviewResource>> GetAllSubsystems()
    {
        var result = new List<SubsystemOverviewResource>
        {
            this.BuildTorrentEngineSubsystem(),
            this.BuildExtractorSubsystem(),
            this.BuildMediaInspectorSubsystem(),
            this.BuildGeoIpSubsystem(),
            this.BuildBlocklistSubsystem(),
            this.BuildNetworkBindingSubsystem(),
            this.BuildMediaMetadataSubsystem(),
            this.BuildHttpTransportSubsystem(),
            this.BuildAiSubsystem(),
        };

        return this.Ok(result);
    }

    [HttpGet("{subsystemId}")]
    public ActionResult<SubsystemOverviewResource> GetSubsystem(string subsystemId)
    {
        var normalized = subsystemId?.ToLowerInvariant();
        var subsystem = normalized switch
        {
            "bittorrent" or "torrentengine" => this.BuildTorrentEngineSubsystem(),
            "extractor" or "archiveextractor" => this.BuildExtractorSubsystem(),
            "mediainspector" or "inspector" => this.BuildMediaInspectorSubsystem(),
            "geoip" => this.BuildGeoIpSubsystem(),
            "blocklist" => this.BuildBlocklistSubsystem(),
            "networkbinding" or "binding" => this.BuildNetworkBindingSubsystem(),
            "mediametadata" or "metadata" => this.BuildMediaMetadataSubsystem(),
            "httptransport" or "transport" => this.BuildHttpTransportSubsystem(),
            "ai" or "intelligence" => this.BuildAiSubsystem(),
            _ => null,
        };

        if (subsystem == null)
        {
            return this.NotFound(new { error = $"Subsystem '{subsystemId}' not found." });
        }

        return this.Ok(subsystem);
    }

    [HttpPost("{subsystemId}/switch")]
    public async Task<ActionResult<SwitchSubsystemProviderResult>> SwitchProvider(string subsystemId, [FromBody] SwitchSubsystemProviderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return this.BadRequest(new SwitchSubsystemProviderResult
            {
                Success = false,
                SubsystemId = subsystemId,
                Error = "ProviderId is required.",
            });
        }

        var normalized = subsystemId?.ToLowerInvariant();
        switch (normalized)
        {
            case "bittorrent" or "torrentengine":
                var torrentRes = await this.torrentEngineManager.SwitchEngineAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = torrentRes.Success,
                    SubsystemId = "bittorrent",
                    PreviousProvider = torrentRes.PreviousEngine,
                    ActiveProvider = torrentRes.ActiveEngine,
                    Message = torrentRes.Message,
                    Error = torrentRes.Error,
                });

            case "extractor" or "archiveextractor":
                var extractRes = await this.extractorManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = extractRes.Success,
                    SubsystemId = "extractor",
                    PreviousProvider = extractRes.PreviousProvider,
                    ActiveProvider = extractRes.ActiveProvider,
                    Message = extractRes.Message,
                    Error = extractRes.Error,
                });

            case "mediainspector" or "inspector":
                var mediaRes = await this.mediaInspectorManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = mediaRes.Success,
                    SubsystemId = "mediainspector",
                    PreviousProvider = mediaRes.PreviousProvider,
                    ActiveProvider = mediaRes.ActiveProvider,
                    Message = mediaRes.Message,
                    Error = mediaRes.Error,
                });

            case "geoip":
                var previousGeo = this.geoIpManager.ActiveProviderId;
                var geoSuccess = await this.geoIpManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = geoSuccess,
                    SubsystemId = "geoip",
                    PreviousProvider = previousGeo,
                    ActiveProvider = this.geoIpManager.ActiveProviderId,
                    Message = geoSuccess ? $"Switched GeoIP provider to {request.ProviderId}." : $"Failed to switch GeoIP provider to {request.ProviderId}.",
                });

            case "blocklist":
                var previousBlock = this.blocklistManager.ActiveProviderId;
                var blockSuccess = await this.blocklistManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = blockSuccess,
                    SubsystemId = "blocklist",
                    PreviousProvider = previousBlock,
                    ActiveProvider = this.blocklistManager.ActiveProviderId,
                    Message = blockSuccess ? $"Switched Blocklist provider to {request.ProviderId}." : $"Failed to switch Blocklist provider to {request.ProviderId}.",
                });

            case "networkbinding" or "binding":
                var netRes = await this.networkBindingManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = netRes.Success,
                    SubsystemId = "networkbinding",
                    PreviousProvider = netRes.PreviousProvider,
                    ActiveProvider = netRes.ActiveProvider,
                    Message = netRes.Message,
                    Error = netRes.Error,
                });

            case "mediametadata" or "metadata":
                var metaRes = await this.mediaMetadataManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = metaRes.Success,
                    SubsystemId = "mediametadata",
                    PreviousProvider = metaRes.PreviousProvider,
                    ActiveProvider = metaRes.ActiveProvider,
                    Message = metaRes.Message,
                    Error = metaRes.Error,
                });

            case "httptransport" or "transport":
                var httpRes = await this.httpTransportManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = httpRes.Success,
                    SubsystemId = "httptransport",
                    PreviousProvider = httpRes.PreviousProvider,
                    ActiveProvider = httpRes.ActiveProvider,
                    Message = httpRes.Message,
                    Error = httpRes.Error,
                });

            case "ai" or "intelligence":
                var previousAi = this.aiManager.ActiveProviderId;
                var aiSuccess = await this.aiManager.SwitchProviderAsync(request.ProviderId);
                return this.Ok(new SwitchSubsystemProviderResult
                {
                    Success = aiSuccess,
                    SubsystemId = "ai",
                    PreviousProvider = previousAi,
                    ActiveProvider = this.aiManager.ActiveProviderId,
                    Message = aiSuccess ? $"Switched AI provider to {request.ProviderId}." : $"Failed to switch AI provider to {request.ProviderId}.",
                });

            default:
                return this.NotFound(new SwitchSubsystemProviderResult
                {
                    Success = false,
                    SubsystemId = subsystemId,
                    Error = $"Subsystem '{subsystemId}' is unknown.",
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
                var torrentProbe = await this.torrentEngineManager.ProbeEngineAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "bittorrent",
                    ProviderId = providerId,
                    IsHealthy = torrentProbe.IsHealthy,
                    StatusMessage = torrentProbe.StatusMessage,
                    DependencyChecks = torrentProbe.DependencyChecks,
                    Warnings = torrentProbe.Warnings,
                });

            case "extractor" or "archiveextractor":
                var extractProbe = await this.extractorManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "extractor",
                    ProviderId = providerId,
                    IsHealthy = extractProbe.IsHealthy,
                    StatusMessage = extractProbe.StatusMessage,
                    DependencyChecks = extractProbe.DependencyChecks,
                    Warnings = extractProbe.Warnings,
                });

            case "mediainspector" or "inspector":
                var mediaProbe = await this.mediaInspectorManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "mediainspector",
                    ProviderId = providerId,
                    IsHealthy = mediaProbe.IsHealthy,
                    StatusMessage = mediaProbe.StatusMessage,
                    DependencyChecks = mediaProbe.DependencyChecks,
                    Warnings = mediaProbe.Warnings,
                });

            case "geoip":
                var geoProbe = await this.geoIpManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "geoip",
                    ProviderId = providerId,
                    IsHealthy = geoProbe.IsHealthy,
                    StatusMessage = geoProbe.StatusMessage,
                    Warnings = geoProbe.Warnings,
                });

            case "blocklist":
                var blockProbe = await this.blocklistManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "blocklist",
                    ProviderId = providerId,
                    IsHealthy = blockProbe.IsHealthy,
                    StatusMessage = blockProbe.StatusMessage,
                    Warnings = blockProbe.Warnings,
                });

            case "networkbinding" or "binding":
                var netProbe = await this.networkBindingManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "networkbinding",
                    ProviderId = providerId,
                    IsHealthy = netProbe.IsHealthy,
                    StatusMessage = netProbe.StatusMessage,
                    Warnings = netProbe.Warnings,
                });

            case "mediametadata" or "metadata":
                var metaProbe = await this.mediaMetadataManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "mediametadata",
                    ProviderId = providerId,
                    IsHealthy = metaProbe.IsHealthy,
                    StatusMessage = metaProbe.StatusMessage,
                    Warnings = metaProbe.Warnings,
                });

            case "httptransport" or "transport":
                var httpProbe = await this.httpTransportManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "httptransport",
                    ProviderId = providerId,
                    IsHealthy = httpProbe.IsHealthy,
                    StatusMessage = httpProbe.StatusMessage,
                    Warnings = httpProbe.Warnings,
                });

            case "ai" or "intelligence":
                var aiProbe = await this.aiManager.ProbeProviderAsync(providerId);
                return this.Ok(new SubsystemProbeResult
                {
                    SubsystemId = "ai",
                    ProviderId = providerId,
                    IsHealthy = aiProbe.IsHealthy,
                    StatusMessage = aiProbe.StatusMessage,
                    Warnings = aiProbe.Warnings,
                });

            default:
                return this.NotFound(new { error = $"Subsystem '{subsystemId}' is unknown." });
        }
    }

    private SubsystemOverviewResource BuildTorrentEngineSubsystem()
    {
        var activeId = this.torrentEngineManager.ActiveEngineId;
        return new SubsystemOverviewResource
        {
            Id = "bittorrent",
            Name = "BitTorrent Engine",
            Category = "Core Download Engine",
            Description = "Primary BitTorrent downloader core managing swarm sessions, piece picking, and disk I/O.",
            ActiveProviderId = activeId,
            Providers = this.torrentEngineManager.GetEngines().Select(e => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildExtractorSubsystem()
    {
        var activeId = this.extractorManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "extractor",
            Name = "Archive Extractor",
            Category = "Post-Processing Pipeline",
            Description = "Unpacks multi-part RAR, 7z, and ZIP archives upon download completion.",
            ActiveProviderId = activeId,
            Providers = this.extractorManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildMediaInspectorSubsystem()
    {
        var activeId = this.mediaInspectorManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "mediainspector",
            Name = "Media Container & Stream Inspector",
            Category = "Media Intelligence",
            Description = "Extracts codecs, HDR formats (Dolby Vision/HDR10+), audio tracks, and container specs.",
            ActiveProviderId = activeId,
            Providers = this.mediaInspectorManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildGeoIpSubsystem()
    {
        var activeId = this.geoIpManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "geoip",
            Name = "Swarm GeoIP Geolocation",
            Category = "Swarm Intelligence",
            Description = "Resolves peer IP addresses into countries, cities, and ISP badges for the peer map visualizer.",
            ActiveProviderId = activeId,
            Providers = this.geoIpManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildBlocklistSubsystem()
    {
        var activeId = this.blocklistManager.ActiveProviderId;
        var totalRules = this.blocklistManager.ActiveProvider?.RuleCount ?? 0;
        return new SubsystemOverviewResource
        {
            Id = "blocklist",
            Name = "IP Blocklist & Threat Intelligence Filter",
            Category = "Network & Security",
            Description = "Filters malicious peer IP addresses, ranges, and CIDR subnets before establishing connections.",
            ActiveProviderId = activeId,
            RuleCount = totalRules,
            Providers = this.blocklistManager.GetProviders().Select(p => new SubsystemProviderResource
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
                    ["supportsLinuxIpSet"] = p.Capabilities.HasFlag(BlocklistCapabilities.LinuxIpSet),
                    ["ruleCount"] = p.RuleCount
                }
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildNetworkBindingSubsystem()
    {
        var activeId = this.networkBindingManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "networkbinding",
            Name = "Network Interface Binding & VPN Kill Switch",
            Category = "Network & Security",
            Description = "Enforces socket routing through specific VPN interfaces (tun0/wg0) with zero traffic leaks.",
            ActiveProviderId = activeId,
            Providers = this.networkBindingManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildMediaMetadataSubsystem()
    {
        var activeId = this.mediaMetadataManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "mediametadata",
            Name = "Media Enrichment & Library Metadata",
            Category = "Media Intelligence",
            Description = "Fetches rich posters, backdrops, season banners, ratings, and cast descriptions for media downloads.",
            ActiveProviderId = activeId,
            Providers = this.mediaMetadataManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildHttpTransportSubsystem()
    {
        var activeId = this.httpTransportManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "httptransport",
            Name = "HTTP Transport & Anti-Bot Engine",
            Category = "Network & Security",
            Description = "Handles tracker announces, RSS sync, and Torznab indexer queries with TLS fingerprinting and challenge solving.",
            ActiveProviderId = activeId,
            Providers = this.httpTransportManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }

    private SubsystemOverviewResource BuildAiSubsystem()
    {
        var activeId = this.aiManager.ActiveProviderId;
        return new SubsystemOverviewResource
        {
            Id = "ai",
            Name = "Artificial Intelligence & Copilot Engine",
            Category = "AI & Smart Automation",
            Description = "Provides natural language search, intelligent scene release de-obfuscation, automated swarm health diagnostics, malware anomaly detection, and conversational Copilot assistance.",
            ActiveProviderId = activeId,
            Providers = this.aiManager.GetProviders().Select(p => new SubsystemProviderResource
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
            }).ToList(),
        };
    }
}
