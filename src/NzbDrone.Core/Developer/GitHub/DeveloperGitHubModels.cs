// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Developer.GitHub;

public class DeveloperPullRequestItem
{
    public long Id { get; set; }

    public int Number { get; set; }

    public string Title { get; set; }

    public string State { get; set; }

    public string Author { get; set; }

    public string AuthorAvatarUrl { get; set; }

    public string HtmlUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime? MergedAt { get; set; }

    public bool Draft { get; set; }

    public int CommentsCount { get; set; }

    public List<string> Labels { get; set; } = new();

    public string BaseBranch { get; set; }

    public string HeadBranch { get; set; }
}

public class DeveloperIssueItem
{
    public long Id { get; set; }

    public int Number { get; set; }

    public string Title { get; set; }

    public string State { get; set; }

    public string Author { get; set; }

    public string AuthorAvatarUrl { get; set; }

    public string HtmlUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int CommentsCount { get; set; }

    public List<string> Labels { get; set; } = new();
}

public class DeveloperGitHubListResult<T>
{
    public string Repository { get; set; }

    public string StateFilter { get; set; }

    public int TotalCount { get; set; }

    public List<T> Items { get; set; } = new();

    public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsRateLimited { get; set; }

    public string Message { get; set; }
}
