// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network;

namespace Leecharr.Api.V1.Network;

[V1ApiController("network")]
public class NetworkController : Controller
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly INetworkStatusService networkStatusService;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly INetworkSecurityService networkSecurityService;
    private readonly ISafeHttpClientService safeHttpClientService;

    public NetworkController(
        INetworkStatusService networkStatusService,
        IConfigService configService = null,
        IDownloadEngine downloadEngine = null,
        INetworkSecurityService networkSecurityService = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.networkStatusService = networkStatusService;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.networkSecurityService = networkSecurityService;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    [HttpGet("status")]
    public ActionResult<NetworkStatus> GetStatus()
    {
        return this.networkStatusService.GetStatus();
    }

    [HttpGet("addresses")]
    public ActionResult<List<string>> GetAddresses()
    {
        var addresses = this.networkStatusService.GetLocalAddresses();
        return this.Ok(addresses);
    }

    [HttpGet("interfaces")]
    public ActionResult<List<string>> GetInterfaces()
    {
        if (this.networkSecurityService != null)
        {
            var interfaces = this.networkSecurityService.GetAvailableNetworkInterfaces()?.ToList();
            if (interfaces != null)
            {
                return this.Ok(interfaces);
            }
        }

        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Select(nic => nic.Name)
                .ToList();
            return this.Ok(interfaces);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to query network interfaces.");
            return this.Ok(new List<string>());
        }
    }

    [HttpPost("test-port")]
    public async Task<ActionResult<PortTestResult>> TestPort([FromBody] PortTestRequest request = null)
    {
        var port = request?.Port > 0
            ? request.Port.Value
            : (this.configService?.ListeningPort > 0 ? this.configService.ListeningPort : 51413);

        if (port < 1 || port > 65535)
        {
            return this.BadRequest("Port must be between 1 and 65535.");
        }

        var isOpen = false;
        string message;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await this.safeHttpClientService.DownloadStringAsync(
                $"https://portcheck.transmissionbt.com/{port}",
                TimeSpan.FromSeconds(5),
                cts.Token);

            if (!string.IsNullOrWhiteSpace(response) && (response.Trim() == "1" || response.Contains('1')))
            {
                isOpen = true;
                message = $"Port {port} is open and reachable from the internet.";
            }
            else
            {
                isOpen = false;
                message = $"Port {port} is closed or unreachable from the internet.";
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Port check failed for port {0}", port);
            isOpen = false;
            message = $"Port {port} check failed: {ex.Message}";
        }

        return this.Ok(new PortTestResult
        {
            Port = port,
            IsOpen = isOpen,
            Message = message,
        });
    }

    [HttpGet("diagnostics")]
    public ActionResult<NetworkDiagnosticsResource> GetDiagnostics()
    {
        var status = this.networkStatusService.GetStatus();
        var listeningPort = this.configService?.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        var uploadSlots = this.configService?.MaxUploadSlots > 0 ? this.configService.MaxUploadSlots : 4;
        var dhtEnabled = this.configService?.EnableDht ?? true;
        var encryptionMode = !string.IsNullOrWhiteSpace(this.configService?.EncryptionMode) ? this.configService.EncryptionMode : "PreferEncrypted";

        var encryptedCount = 0;
        var plaintextCount = 0;
        var activeConnections = 0;

        if (this.downloadEngine != null)
        {
            try
            {
                var tasks = this.downloadEngine.GetAllTasks();
                if (tasks != null)
                {
                    foreach (var task in tasks)
                    {
                        var peers = task.GetPeers();
                        if (peers != null)
                        {
                            activeConnections += peers.Count;
                            foreach (var peer in peers)
                            {
                                if (peer.IsEncrypted)
                                {
                                    encryptedCount++;
                                }
                                else
                                {
                                    plaintextCount++;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Engine query fallback
            }
        }

        var totalPeers = encryptedCount + plaintextCount;
        var encPct = totalPeers > 0
            ? Math.Round((double)encryptedCount / totalPeers * 100.0, 1)
            : 100.0;

        var portMappings = status.PortMappings?.Select(pm => new PortMappingResource
        {
            InternalPort = pm.InternalPort,
            ExternalPort = pm.ExternalPort,
            Protocol = pm.Protocol,
            Description = pm.Description,
            IsActive = pm.IsActive,
        }).ToList() ?? new List<PortMappingResource>();

        var dhtNodeCount = this.downloadEngine?.DhtNodeCount ?? 0;

        return this.Ok(new NetworkDiagnosticsResource
        {
            LocalIp = status.LocalIp ?? "127.0.0.1",
            ExternalIp = status.ExternalIp ?? string.Empty,
            LocalAddresses = status.LocalAddresses ?? new List<string>(),
            UpnpAvailable = status.UpnpAvailable,
            ProxyEnabled = status.ProxyEnabled,
            PortMappings = portMappings,
            ListeningPort = listeningPort,
            ActiveConnections = activeConnections,
            UploadSlots = uploadSlots,
            DhtEnabled = dhtEnabled,
            DhtNodeCount = dhtNodeCount,
            EncryptionMode = encryptionMode,
            EncryptedConnections = encryptedCount,
            PlaintextConnections = plaintextCount,
            EncryptionPercentage = encPct,
        });
    }
}
