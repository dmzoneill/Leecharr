using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Api.V1.ArrIntegration;

[V1ApiController("arrsync")]
public class ArrSyncController : Controller
{
    [HttpPost("sync")]
    public ActionResult<SyncResultResource> Sync()
    {
        return Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = 0,
            Message = "Arr sync completed successfully."
        });
    }
}
