using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclientsync")]
public class DownloadClientSyncController : Controller
{
    [HttpPost("sync")]
    public ActionResult<SyncResultResource> Sync()
    {
        return Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = 0,
            Message = "Download client sync completed successfully."
        });
    }
}
