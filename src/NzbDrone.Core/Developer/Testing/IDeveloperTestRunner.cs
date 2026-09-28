// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Developer.Testing;

public class DeveloperTestItem
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TargetComponent { get; set; } = string.Empty;
}

public class DeveloperTestResult
{
    public string TestId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Status { get; set; } = "Passed";

    public long DurationMs { get; set; }

    public string Output { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public string StackTrace { get; set; } = string.Empty;

    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}

public class TestExecutionRequest
{
    public List<string> TestIds { get; set; } = new();

    public string Category { get; set; } = string.Empty;

    public bool RunAll { get; set; }
}

public class TestExecutionResponse
{
    public int TotalTests { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public long TotalDurationMs { get; set; }

    public List<DeveloperTestResult> Results { get; set; } = new();
}

public interface IDeveloperTestRunner
{
    IReadOnlyList<DeveloperTestItem> DiscoverTests();

    Task<DeveloperTestResult> RunTestAsync(string testId);

    Task<TestExecutionResponse> RunTestsAsync(TestExecutionRequest request);

    IReadOnlyList<DeveloperTestResult> GetRecentResults(int limit = 50);

    void ClearHistory();
}
