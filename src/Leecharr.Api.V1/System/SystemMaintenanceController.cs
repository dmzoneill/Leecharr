using System;
using System.Threading.Tasks;
using Dapper;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Datastore;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/maintenance")]
public class SystemMaintenanceController : ControllerBase
{
    private readonly IDatabase _database;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public SystemMaintenanceController(IDatabase database)
    {
        _database = database;
    }

    [HttpPost("vacuum")]
    public async Task<ActionResult> Vacuum()
    {
        try
        {
            _logger.Info("Starting database compaction (VACUUM)...");
            using var connection = _database.OpenConnection();
            await connection.ExecuteAsync("VACUUM;");
            _logger.Info("Database compaction (VACUUM) completed successfully.");
            return Ok(new { Success = true, Message = "Database VACUUM completed successfully." });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error running database VACUUM");
            return StatusCode(500, new { Success = false, Message = ex.Message });
        }
    }
}
