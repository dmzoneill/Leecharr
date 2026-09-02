// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Network;

namespace Leecharr.Api.V1.Network;

[V1ApiController("network")]
public class NetworkController : Controller
{
    private readonly INetworkStatusService networkStatusService;

    public NetworkController(INetworkStatusService networkStatusService)
    {
        this.networkStatusService = networkStatusService;
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
}
