// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IDatabase database;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public SystemMaintenanceController(IDatabase database)
    {
        this.database = database;
    }

    [HttpPost("vacuum")]
    public async Task<ActionResult> Vacuum()
    {
        try
        {
            this.logger.Info("Starting database compaction (VACUUM)...");
            using var connection = this.database.OpenConnection();
            await connection.ExecuteAsync("VACUUM;");
            this.logger.Info("Database compaction (VACUUM) completed successfully.");
            return this.Ok(new { Success = true, Message = "Database VACUUM completed successfully." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error running database VACUUM");
            return this.StatusCode(500, new { Success = false, Message = ex.Message });
        }
    }
}
