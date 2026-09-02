// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;

namespace Leecharr.Api.V1.Network;

[V1ApiController("network")]
public class NetworkController : Controller
{
    private readonly INetworkStatusService networkStatusService;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;

    public NetworkController(
        INetworkStatusService networkStatusService,
        IConfigService configService = null,
        IDownloadEngine downloadEngine = null)
    {
        this.networkStatusService = networkStatusService;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
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
