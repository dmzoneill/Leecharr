// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Update;

public class UpdateCheckService : IUpdateCheckService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/dmzoneill/Leecharr/releases?per_page=100";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly HttpClient httpClient;
    private readonly Logger logger;
    private readonly SemaphoreSlim cacheLock = new(1, 1);

    private List<UpdatePackage> cachedUpdates;
    private DateTime lastFetchTime = DateTime.MinValue;

    public UpdateCheckService(HttpClient httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<List<UpdatePackage>> GetAvailableUpdatesAsync(CancellationToken cancellationToken = default)
    {
        await this.cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.cachedUpdates != null && (DateTime.UtcNow - this.lastFetchTime) < CacheTtl)
            {
                return this.cachedUpdates;
            }

            var updates = await this.FetchFromGitHubAsync(cancellationToken).ConfigureAwait(false);
            if (updates != null && updates.Count > 0)
            {
                this.cachedUpdates = updates;
                this.lastFetchTime = DateTime.UtcNow;
                return this.cachedUpdates;
            }

            if (this.cachedUpdates != null)
            {
                return this.cachedUpdates;
            }

            return this.GetDefaultInstalledReleaseList();
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    private async Task<List<UpdatePackage>> FetchFromGitHubAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl);
            var versionString = BuildInfo.Version?.ToString() ?? "1.0.0";
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr", versionString));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("GitHub releases check returned status code: {0}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<UpdatePackage>();
            var currentVer = BuildInfo.Version?.ToString() ?? "1.0.0";
            var isFirst = true;

            foreach (var elem in root.EnumerateArray())
            {
                var tagName = elem.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                var cleanVersion = tagName.TrimStart('v', 'V');
                var publishedAt = elem.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt)
                    ? dt
                    : (elem.TryGetProperty("created_at", out var createProp) && createProp.TryGetDateTime(out var cdt) ? cdt : DateTime.UtcNow);

                var htmlUrl = elem.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
                var body = elem.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : string.Empty;

                string fileName = $"Leecharr.{cleanVersion}.linux-x64.tar.gz";
                if (elem.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameProp))
                        {
                            var nameStr = nameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(nameStr) && nameStr.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                            {
                                fileName = nameStr;
                                break;
                            }
                        }
                    }
                }

                var isInstalled = string.Equals(cleanVersion, currentVer, StringComparison.OrdinalIgnoreCase) ||
                                  cleanVersion.StartsWith(currentVer, StringComparison.OrdinalIgnoreCase);

                result.Add(new UpdatePackage
                {
                    Version = cleanVersion,
                    ReleaseDate = publishedAt,
                    FileName = fileName,
                    Url = htmlUrl ?? "https://github.com/dmzoneill/Leecharr/releases",
                    Installed = isInstalled,
                    Latest = isFirst,
                    Changes = ParseChanges(body),
                });

                isFirst = false;
            }

            return result;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to fetch updates from GitHub releases");
            return null;
        }
    }

    private List<UpdatePackage> GetDefaultInstalledReleaseList()
    {
        var currentVersion = BuildInfo.Version?.ToString() ?? "1.0.25";
        return new List<UpdatePackage>
        {
            new()
            {
                Version = currentVersion,
                ReleaseDate = DateTime.UtcNow,
                FileName = $"Leecharr.{currentVersion}.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases",
                Installed = true,
                Latest = true,
                Changes = new UpdatePackageChanges
                {
                    New = new List<string>
                    {
                        "Integrated health diagnostics and telemetry updates",
                    },
                    Fixed = new List<string>(),
                },
            },
        };
    }

    private static UpdatePackageChanges ParseChanges(string body)
    {
        var changes = new UpdatePackageChanges();
        if (string.IsNullOrWhiteSpace(body))
        {
            return changes;
        }

        var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var currentSection = "new";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#'))
            {
                var header = line.TrimStart('#').Trim().ToLowerInvariant();
                if (header.Contains("fix") || header.Contains("bug"))
                {
                    currentSection = "fixed";
                }
                else
                {
                    currentSection = "new";
                }

                continue;
            }

            if (line.StartsWith('-') || line.StartsWith('*'))
            {
                var item = line.TrimStart('-', '*', ' ').Trim();
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                if (currentSection == "fixed")
                {
                    changes.Fixed.Add(item);
                }
                else
                {
                    changes.New.Add(item);
                }
            }
        }

        return changes;
    }
}
