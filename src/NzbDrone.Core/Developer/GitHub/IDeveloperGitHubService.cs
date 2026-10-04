// Copyright (c) FeedItOut. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Developer.GitHub;

public interface IDeveloperGitHubService
{
    Task<DeveloperGitHubListResult<DeveloperPullRequestItem>> GetPullRequestsAsync(string state = "all", CancellationToken cancellationToken = default);

    Task<DeveloperGitHubListResult<DeveloperIssueItem>> GetIssuesAsync(string state = "all", CancellationToken cancellationToken = default);
}
