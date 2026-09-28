// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Developer.Testing;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/developer/testing")]
public class SystemDeveloperTestingController : Controller
{
    private readonly IDeveloperTestRunner testRunner;

    public SystemDeveloperTestingController(IDeveloperTestRunner testRunner = null)
    {
        this.testRunner = testRunner;
    }

    [HttpGet("tests")]
    public ActionResult<IReadOnlyList<DeveloperTestItem>> GetTests()
    {
        if (this.testRunner == null)
        {
            return new List<DeveloperTestItem>();
        }

        return this.Ok(this.testRunner.DiscoverTests());
    }

    [HttpPost("run")]
    public async Task<ActionResult<TestExecutionResponse>> RunTests([FromBody] TestExecutionRequest request)
    {
        if (this.testRunner == null)
        {
            return new TestExecutionResponse();
        }

        var response = await this.testRunner.RunTestsAsync(request ?? new TestExecutionRequest { RunAll = true });
        return this.Ok(response);
    }

    [HttpPost("run/{testId}")]
    public async Task<ActionResult<DeveloperTestResult>> RunSingleTest(string testId)
    {
        if (this.testRunner == null)
        {
            return new DeveloperTestResult { TestId = testId, Status = "Skipped" };
        }

        var result = await this.testRunner.RunTestAsync(testId);
        return this.Ok(result);
    }

    [HttpGet("history")]
    public ActionResult<IReadOnlyList<DeveloperTestResult>> GetHistory([FromQuery] int limit = 50)
    {
        if (this.testRunner == null)
        {
            return new List<DeveloperTestResult>();
        }

        return this.Ok(this.testRunner.GetRecentResults(limit));
    }

    [HttpDelete("history")]
    public ActionResult ClearHistory()
    {
        this.testRunner?.ClearHistory();
        return this.NoContent();
    }
}
