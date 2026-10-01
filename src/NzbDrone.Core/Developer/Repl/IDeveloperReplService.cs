// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Developer.Repl;

public class ReplExecutionRequest
{
    public string Code { get; set; } = string.Empty;

    public string Language { get; set; } = "javascript";

    public int TimeoutSeconds { get; set; } = 15;
}

public class ReplExecutionResponse
{
    public bool Success { get; set; }

    public string ResultJson { get; set; }

    public string ResultType { get; set; }

    public string Output { get; set; } = string.Empty;

    public string ErrorMessage { get; set; }

    public long DurationMs { get; set; }
}

public class ReplHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Code { get; set; } = string.Empty;

    public string Language { get; set; } = "javascript";

    public bool Success { get; set; }

    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;

    public long DurationMs { get; set; }
}

public interface IDeveloperReplService
{
    ReplExecutionResponse Execute(ReplExecutionRequest request);

    IReadOnlyList<ReplHistoryEntry> GetHistory(int limit = 50);

    void ClearHistory();

    void ResetSession();
}
