using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Network;

namespace Leecharr.Api.V1.Network;

[V1ApiController("network")]
public class NetworkController : Controller
{
    private readonly INetworkStatusService _networkStatusService;

    public NetworkController(INetworkStatusService networkStatusService)
    {
        _networkStatusService = networkStatusService;
    }

    [HttpGet("status")]
    public ActionResult<NetworkStatus> GetStatus()
    {
        return _networkStatusService.GetStatus();
    }

    [HttpGet("addresses")]
    public ActionResult<List<string>> GetAddresses()
    {
        var addresses = _networkStatusService.GetLocalAddresses();
        return Ok(addresses);
    }
}
