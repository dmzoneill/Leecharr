// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Developer.Quality;

public class DeveloperQualityService : IDeveloperQualityService
{
    private const string ProjectKey = "dmzoneill_Leecharr";
    private const string RepoOwner = "dmzoneill";
    private const string RepoName = "Leecharr";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly string[] QualityWorkflowNames =
    {
        "SonarCloud Analysis",
        "Codacy Security Scan",
        "CodeQL Analysis",
        "CICD"
    };
    private static readonly object CacheLock = new();
    private static DeveloperQualityReport _cachedReport;
    private static DateTime _cacheExpiry = DateTime.MinValue;

    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public DeveloperQualityService(HttpClient httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static void ResetCache()
    {
        lock (CacheLock)
        {
            _cachedReport = null;
            _cacheExpiry = DateTime.MinValue;
        }
    }

    public async Task<DeveloperQualityReport> GetQualityReportAsync(CancellationToken cancellationToken = default)
    {
        lock (CacheLock)
        {
            if (_cachedReport != null && _cacheExpiry > DateTime.UtcNow)
            {
                return _cachedReport;
            }
        }

        var report = new DeveloperQualityReport
        {
            ProjectName = "Leecharr",
            Repository = $"{RepoOwner}/{RepoName}",
            SonarCloudUrl = $"https://sonarcloud.io/dashboard?id={ProjectKey}",
            CodacyUrl = $"https://app.codacy.com/gh/{RepoOwner}/{RepoName}/dashboard",
            GitHubRepoUrl = $"https://github.com/{RepoOwner}/{RepoName}"
        };

        await PopulateSonarQualityGateAsync(report, cancellationToken);
        await PopulateSonarMeasuresAsync(report, cancellationToken);
        await PopulateGitHubWorkflowsAsync(report, cancellationToken);

        lock (CacheLock)
        {
            if (ShouldPublishCachedReport(report))
            {
                _cachedReport = report;
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
                return report;
            }

            if (HasFreshLiveCache() && GetReportSourceScore(_cachedReport) >= GetReportSourceScore(report))
            {
                return _cachedReport;
            }

            return report;
        }
    }

    private static bool HasFreshLiveCache()
    {
        return _cachedReport != null
            && _cacheExpiry > DateTime.UtcNow
            && !_cachedReport.UsedFallbackQualityGate
            && !_cachedReport.UsedFallbackPipelines;
    }

    private static bool ShouldPublishCachedReport(DeveloperQualityReport incoming)
    {
        if (incoming == null || incoming.UsedFallbackQualityGate || incoming.UsedFallbackPipelines)
        {
            return false;
        }

        if (_cachedReport == null || _cacheExpiry <= DateTime.UtcNow)
        {
            return true;
        }

        return GetReportSourceScore(incoming) >= GetReportSourceScore(_cachedReport);
    }

    private static int GetReportSourceScore(DeveloperQualityReport report)
    {
        if (report == null)
        {
            return 0;
        }

        var score = 0;
        if (!report.UsedFallbackQualityGate)
        {
            score += 2;
        }

        if (!report.UsedFallbackPipelines)
        {
            score += 2;
        }

        if (report.QualityGate.Conditions.Count > 0)
        {
            score += 1;
        }

        return score;
    }

    private async Task PopulateSonarQualityGateAsync(DeveloperQualityReport report, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://sonarcloud.io/api/qualitygates/project_status?projectKey={ProjectKey}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr-Quality-Scanner", "2.0.31"));

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.Debug("SonarCloud Quality Gate API returned status {0}; quality gate is unavailable", (int)resp.StatusCode);
                MarkQualityGateUnavailable(report);
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projectStatus", out var ps))
            {
                MarkQualityGateUnavailable(report);
                return;
            }

            var gateStatus = ps.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(gateStatus))
            {
                MarkQualityGateUnavailable(report);
                return;
            }

            report.QualityGate.Status = gateStatus;
            report.QualityGateStatusFromSonarApi = true;
            report.UsedFallbackQualityGate = false;
            if (ps.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in conds.EnumerateArray())
                {
                    var metricKey = c.TryGetProperty("metricKey", out var mk) ? mk.GetString() : string.Empty;
                    var status = c.TryGetProperty("status", out var st) ? st.GetString() : "NONE";
                    var actual = c.TryGetProperty("actualValue", out var act) ? act.GetString() : string.Empty;
                    var threshold = c.TryGetProperty("errorThreshold", out var eth) ? eth.GetString() : string.Empty;
                    var comparator = c.TryGetProperty("comparator", out var comp) ? comp.GetString() : string.Empty;

                    report.QualityGate.Conditions.Add(new QualityGateMetric
                    {
                        MetricKey = metricKey,
                        Status = status,
                        ActualValue = actual,
                        ErrorThreshold = threshold,
                        Comparator = comparator
                    });

                    if (metricKey == "new_coverage" && double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var newCov))
                    {
                        report.Metrics.NewCodeCoverage = newCov;
                    }
                    else if (metricKey == "new_duplicated_lines_density" && double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var newDup))
                    {
                        report.Metrics.NewCodeDuplicationDensity = newDup;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query SonarCloud Quality Gate API; quality gate is unavailable");
            MarkQualityGateUnavailable(report);
        }
    }

    private async Task PopulateSonarMeasuresAsync(DeveloperQualityReport report, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://sonarcloud.io/api/measures/component?component={ProjectKey}&metricKeys=alert_status,bugs,vulnerabilities,code_smells,coverage,duplicated_lines_density,sqale_index,reliability_rating,security_rating,sqale_rating,security_hotspots_reviewed";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr-Quality-Scanner", "2.0.31"));

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("component", out var comp) &&
                    comp.TryGetProperty("measures", out var measures) &&
                    measures.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in measures.EnumerateArray())
                    {
                        var key = m.TryGetProperty("metric", out var k) ? k.GetString() : string.Empty;
                        var val = m.TryGetProperty("value", out var v) ? v.GetString() : string.Empty;

                        switch (key)
                        {
                            case "alert_status" when !report.QualityGateStatusFromSonarApi && !string.IsNullOrWhiteSpace(val):
                                report.QualityGate.Status = val;
                                report.UsedFallbackQualityGate = false;
                                report.Message = null;
                                break;
                            case "coverage" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var c):
                                report.Metrics.Coverage = c;
                                break;
                            case "duplicated_lines_density" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var dup):
                                report.Metrics.DuplicationDensity = dup;
                                break;
                            case "bugs" when int.TryParse(val, out var b):
                                report.Metrics.Bugs = b;
                                break;
                            case "vulnerabilities" when int.TryParse(val, out var vul):
                                report.Metrics.Vulnerabilities = vul;
                                break;
                            case "code_smells" when int.TryParse(val, out var cs):
                                report.Metrics.CodeSmells = cs;
                                break;
                            case "security_hotspots_reviewed" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hr):
                                report.Metrics.SecurityHotspotsReviewed = hr;
                                break;
                            case "reliability_rating":
                                report.Metrics.ReliabilityRating = ConvertRating(val);
                                break;
                            case "security_rating":
                                report.Metrics.SecurityRating = ConvertRating(val);
                                break;
                            case "sqale_rating":
                                report.Metrics.MaintainabilityRating = ConvertRating(val);
                                break;
                            case "sqale_index" when int.TryParse(val, out var sq):
                                report.Metrics.TechnicalDebtFormatted = $"{sq} min";
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query SonarCloud measures API");
        }
    }

    private async Task PopulateGitHubWorkflowsAsync(DeveloperQualityReport report, CancellationToken cancellationToken)
    {
        try
        {
            var workflowIds = await GetQualityWorkflowIdsAsync(cancellationToken);
            if (workflowIds == null)
            {
                SetFallbackPipelines(report);
                return;
            }

            var runRequestFailed = false;
            foreach (var workflowName in QualityWorkflowNames)
            {
                if (!workflowIds.TryGetValue(workflowName, out var workflowId))
                {
                    continue;
                }

                if (!await TryAppendLatestWorkflowRunAsync(report, workflowId, cancellationToken))
                {
                    runRequestFailed = true;
                }
            }

            if (!runRequestFailed)
            {
                return;
            }

            if (report.RecentPipelines.Count > 0)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query GitHub workflow runs API; pipeline history is unavailable");
        }

        SetFallbackPipelines(report);
    }

    private async Task<Dictionary<string, long>> GetQualityWorkflowIdsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/actions/workflows?per_page=100";
        using var req = CreateGitHubApiRequest(url);
        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.Debug("GitHub workflows API returned status {0}; pipeline history is unavailable", (int)resp.StatusCode);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("workflows", out var workflows) || workflows.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var workflow in workflows.EnumerateArray())
        {
            var name = workflow.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name) || !workflow.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            result[name] = idElement.GetInt64();
        }

        return result;
    }

    private async Task<bool> TryAppendLatestWorkflowRunAsync(DeveloperQualityReport report, long workflowId, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/actions/workflows/{workflowId}/runs?per_page=1";
        using var req = CreateGitHubApiRequest(url);
        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.Debug("GitHub workflow {0} runs API returned status {1}", workflowId, (int)resp.StatusCode);
            return false;
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var latestRun = runs.EnumerateArray().FirstOrDefault();
        if (latestRun.ValueKind != JsonValueKind.Undefined)
        {
            report.RecentPipelines.Add(ParseWorkflowRun(latestRun));
        }

        return true;
    }

    private static HttpRequestMessage CreateGitHubApiRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr-Quality-Scanner", "2.0.31"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        return req;
    }

    private static QualityPipelineRun ParseWorkflowRun(JsonElement r)
    {
        var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "Workflow" : "Workflow";
        var status = r.TryGetProperty("status", out var st) ? st.GetString() ?? "completed" : "completed";
        string conclusion = null;
        if (r.TryGetProperty("conclusion", out var conc) && conc.ValueKind != JsonValueKind.Null)
        {
            conclusion = conc.GetString();
        }
        var number = r.TryGetProperty("run_number", out var num) ? num.GetInt32() : 0;
        var htmlUrl = r.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? string.Empty : string.Empty;
        var createdAt = r.TryGetProperty("created_at", out var ca) ? ca.GetDateTime() : DateTime.UtcNow;

        return new QualityPipelineRun
        {
            Name = name,
            Status = status,
            Conclusion = conclusion,
            RunNumber = number,
            HtmlUrl = htmlUrl,
            CreatedAt = createdAt
        };
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    private static string ConvertRating(string raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
        {
            return num switch
            {
                <= 1.0 => "A",
                <= 2.0 => "B",
                <= 3.0 => "C",
                <= 4.0 => "D",
                _ => "E"
            };
        }
        return "A";
    }

    private static void MarkQualityGateUnavailable(DeveloperQualityReport report)
    {
        report.UsedFallbackQualityGate = true;
        report.QualityGate.Status = "NONE";
        report.QualityGate.Conditions.Clear();
        report.Message = "SonarCloud quality gate is unavailable.";
    }

    private static void SetFallbackPipelines(DeveloperQualityReport report)
    {
        report.UsedFallbackPipelines = true;
        report.RecentPipelines.Clear();
        if (string.IsNullOrWhiteSpace(report.Message))
        {
            report.Message = "GitHub Actions pipeline history is unavailable.";
            return;
        }

        if (!report.Message.Contains("pipeline history", StringComparison.OrdinalIgnoreCase))
        {
            report.Message = $"{report.Message} GitHub Actions pipeline history is unavailable.";
        }
    }
}
