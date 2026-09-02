// Copyright (c) PlaceholderCompany. All rights reserved.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Http.Ping;

[AllowAnonymous]
[ApiController]
[Route("ping")]
public class PingController : ControllerBase
{
    [HttpGet]
    public ActionResult<PingResource> Ping()
    {
        return this.Ok(new PingResource { Status = "OK" });
    }
}

public class PingResource
{
    public string Status { get; set; }
}
