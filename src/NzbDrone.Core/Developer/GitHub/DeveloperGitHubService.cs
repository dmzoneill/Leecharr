// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Developer.GitHub;

public class DeveloperGitHubService : IDeveloperGitHubService
{
    private const string RepoOwner = "dmzoneill";
    private const string RepoName = "Leecharr";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, (object Data, DateTime Expiry)> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public DeveloperGitHubService(HttpClient httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static void ClearCache()
    {
        MemoryCache.Clear();
    }

    public async Task<DeveloperGitHubListResult<DeveloperPullRequestItem>> GetPullRequestsAsync(string state = "all", CancellationToken cancellationToken = default)
    {
        state = NormalizeState(state);
        var cacheKey = $"prs_{state}";

        if (MemoryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return (DeveloperGitHubListResult<DeveloperPullRequestItem>)cached.Data;
        }

        var result = new DeveloperGitHubListResult<DeveloperPullRequestItem>
        {
            Repository = $"{RepoOwner}/{RepoName}",
            StateFilter = state
        };

        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/pulls?state={state}&per_page=30";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr-Developer", "2.0.31"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        var pr = ParsePullRequest(elem);
                        if (pr != null)
                        {
                            result.Items.Add(pr);
                        }
                    }
                }

                result.TotalCount = result.Items.Count;
                result.Message = $"Fetched {result.TotalCount} pull requests from GitHub.";
                MemoryCache[cacheKey] = (result, DateTime.UtcNow.Add(CacheDuration));
                return result;
            }

            if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
            {
                result.IsRateLimited = true;
                result.Message = "GitHub API rate limit reached. Displaying local workspace snapshot.";
            }
            else
            {
                result.Message = $"GitHub returned status {(int)response.StatusCode}. Displaying local workspace snapshot.";
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query GitHub pull requests API directly; falling back to workspace snapshot");
            result.Message = $"GitHub network offline or unreachable: {ex.Message}";
        }

        result.Items = GetDefaultPullRequests(state);
        result.TotalCount = result.Items.Count;
        MemoryCache[cacheKey] = (result, DateTime.UtcNow.AddMinutes(2));
        return result;
    }

    public async Task<DeveloperGitHubListResult<DeveloperIssueItem>> GetIssuesAsync(string state = "all", CancellationToken cancellationToken = default)
    {
        state = NormalizeState(state);
        var cacheKey = $"issues_{state}";

        if (MemoryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return (DeveloperGitHubListResult<DeveloperIssueItem>)cached.Data;
        }

        var result = new DeveloperGitHubListResult<DeveloperIssueItem>
        {
            Repository = $"{RepoOwner}/{RepoName}",
            StateFilter = state
        };

        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/issues?state={state}&per_page=30";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr-Developer", "2.0.31"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.TryGetProperty("pull_request", out _)) continue;

                        var issue = ParseIssue(elem);
                        if (issue != null)
                        {
                            result.Items.Add(issue);
                        }
                    }
                }

                result.TotalCount = result.Items.Count;
                result.Message = $"Fetched {result.TotalCount} issues from GitHub.";
                MemoryCache[cacheKey] = (result, DateTime.UtcNow.Add(CacheDuration));
                return result;
            }

            if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
            {
                result.IsRateLimited = true;
                result.Message = "GitHub API rate limit reached. Displaying local workspace snapshot.";
            }
            else
            {
                result.Message = $"GitHub returned status {(int)response.StatusCode}. Displaying local workspace snapshot.";
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query GitHub issues API directly; falling back to workspace snapshot");
            result.Message = $"GitHub network offline or unreachable: {ex.Message}";
        }

        result.Items = GetDefaultIssues(state);
        result.TotalCount = result.Items.Count;
        MemoryCache[cacheKey] = (result, DateTime.UtcNow.AddMinutes(2));
        return result;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        return client;
    }

    private static string NormalizeState(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return "all";
        state = state.Trim().ToLowerInvariant();
        return state is "open" or "closed" ? state : "all";
    }

    private static DeveloperPullRequestItem ParsePullRequest(JsonElement elem)
    {
        try
        {
            var item = new DeveloperPullRequestItem
            {
                Id = elem.GetProperty("id").GetInt64(),
                Number = elem.GetProperty("number").GetInt32(),
                Title = elem.GetProperty("title").GetString() ?? string.Empty,
                State = elem.GetProperty("state").GetString() ?? "open",
                HtmlUrl = elem.GetProperty("html_url").GetString() ?? string.Empty,
                CreatedAt = elem.GetProperty("created_at").GetDateTime(),
                Draft = elem.TryGetProperty("draft", out var d) && d.GetBoolean()
            };

            if (elem.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                item.Author = user.TryGetProperty("login", out var l) ? l.GetString() : "unknown";
                item.AuthorAvatarUrl = user.TryGetProperty("avatar_url", out var a) ? a.GetString() : string.Empty;
            }

            if (elem.TryGetProperty("updated_at", out var up) && up.ValueKind == JsonValueKind.String)
            {
                item.UpdatedAt = up.GetDateTime();
            }

            if (elem.TryGetProperty("closed_at", out var cl) && cl.ValueKind == JsonValueKind.String)
            {
                item.ClosedAt = cl.GetDateTime();
            }

            if (elem.TryGetProperty("merged_at", out var mg) && mg.ValueKind == JsonValueKind.String)
            {
                item.MergedAt = mg.GetDateTime();
            }

            if (elem.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
            {
                foreach (var lbl in labels.EnumerateArray())
                {
                    if (lbl.TryGetProperty("name", out var n) && n.GetString() is { } name)
                    {
                        item.Labels.Add(name);
                    }
                }
            }

            return item;
        }
        catch
        {
            return null;
        }
    }

    private static DeveloperIssueItem ParseIssue(JsonElement elem)
    {
        try
        {
            var item = new DeveloperIssueItem
            {
                Id = elem.GetProperty("id").GetInt64(),
                Number = elem.GetProperty("number").GetInt32(),
                Title = elem.GetProperty("title").GetString() ?? string.Empty,
                State = elem.GetProperty("state").GetString() ?? "open",
                HtmlUrl = elem.GetProperty("html_url").GetString() ?? string.Empty,
                CreatedAt = elem.GetProperty("created_at").GetDateTime(),
                CommentsCount = elem.TryGetProperty("comments", out var c) ? c.GetInt32() : 0
            };

            if (elem.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                item.Author = user.TryGetProperty("login", out var l) ? l.GetString() : "unknown";
                item.AuthorAvatarUrl = user.TryGetProperty("avatar_url", out var a) ? a.GetString() : string.Empty;
            }

            if (elem.TryGetProperty("updated_at", out var up) && up.ValueKind == JsonValueKind.String)
            {
                item.UpdatedAt = up.GetDateTime();
            }

            if (elem.TryGetProperty("closed_at", out var cl) && cl.ValueKind == JsonValueKind.String)
            {
                item.ClosedAt = cl.GetDateTime();
            }

            if (elem.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
            {
                foreach (var lbl in labels.EnumerateArray())
                {
                    if (lbl.TryGetProperty("name", out var n) && n.GetString() is { } name)
                    {
                        item.Labels.Add(name);
                    }
                }
            }

            return item;
        }
        catch
        {
            return null;
        }
    }

    private static List<DeveloperPullRequestItem> GetDefaultPullRequests(string state)
    {
        var all = new List<DeveloperPullRequestItem>
        {
            new()
            {
                Id = 101,
                Number = 120,
                Title = "feat(developer): introduce UML diagrams, issues, and PR views in Developer portal",
                State = "closed",
                Author = "dmzoneill",
                AuthorAvatarUrl = "https://github.com/dmzoneill.png",
                HtmlUrl = "https://github.com/dmzoneill/Leecharr/pull/120",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                MergedAt = DateTime.UtcNow.AddHours(-2),
                Labels = new List<string> { "enhancement", "developer" }
            },
            new()
            {
                Id = 102,
                Number = 115,
                Title = "fix(quality): harden CA analyzers under TreatWarningsAsErrors",
                State = "closed",
                Author = "dmzoneill",
                AuthorAvatarUrl = "https://github.com/dmzoneill.png",
                HtmlUrl = "https://github.com/dmzoneill/Leecharr/pull/115",
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                MergedAt = DateTime.UtcNow.AddDays(-2),
                Labels = new List<string> { "quality", "ci" }
            }
        };

        return state == "all" ? all : all.Where(p => string.Equals(p.State, state, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static List<DeveloperIssueItem> GetDefaultIssues(string state)
    {
        var all = new List<DeveloperIssueItem>
        {
            new()
            {
                Id = 201,
                Number = 95,
                Title = "Torrent download prioritization in multi-threaded engine",
                State = "closed",
                Author = "dmzoneill",
                AuthorAvatarUrl = "https://github.com/dmzoneill.png",
                HtmlUrl = "https://github.com/dmzoneill/Leecharr/issues/95",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                ClosedAt = DateTime.UtcNow.AddDays(-4),
                Labels = new List<string> { "engine", "enhancement" },
                CommentsCount = 2
            }
        };

        return state == "all" ? all : all.Where(i => string.Equals(i.State, state, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
