// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly char[] NewlineSeparators = ['\r', '\n'];

    private readonly HttpClient httpClient;
    private readonly Logger logger;
    private readonly SemaphoreSlim cacheLock = new(1, 1);

    private List<UpdatePackage> cachedUpdates;
    private DateTime lastFetchTime = DateTime.MinValue;

    public UpdateCheckService(HttpClient httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public static string GetPackagePlatformIdentifier()
    {
        return GetPackagePlatformIdentifier(OsInfo.IsWindows, OsInfo.IsOsx, RuntimeInformation.ProcessArchitecture);
    }

    public static string GetPackagePlatformIdentifier(bool isWindows, bool isOsx, Architecture architecture)
    {
        var os = isWindows ? "win" :
                    isOsx ? "osx" : "linux";

        var arch = architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        return $"{os}-{arch}";
    }

    public static string GetPackageExtension()
    {
        return GetPackageExtension(OsInfo.IsWindows);
    }

    public static string GetPackageExtension(bool isWindows)
    {
        return isWindows ? ".zip" : ".tar.gz";
    }

    public static string GetPackageFileName(string version)
    {
        return $"Leecharr.{version}.{GetPackagePlatformIdentifier()}{GetPackageExtension()}";
    }

    public static string SelectPackageAssetFileName(IEnumerable<string> assetNames, string platformIdentifier, string packageExtension, string fallbackFileName)
    {
        if (assetNames == null)
        {
            return fallbackFileName;
        }

        var expectedSuffix = platformIdentifier + packageExtension;
        foreach (var name in assetNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return fallbackFileName;
    }

    public static string BuildGitHubAssetDownloadUrl(string tagName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var encodedFileName = Uri.EscapeDataString(fileName);
        return $"https://github.com/dmzoneill/Leecharr/releases/download/{tagName.Trim()}/{encodedFileName}";
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

            return new List<UpdatePackage>();
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl);
            var versionString = BuildInfo.Version?.ToString() ?? "1.0.0";
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Leecharr", versionString));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await this.httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("GitHub releases check returned status code: {0}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<UpdatePackage>();
            var currentVer = BuildInfo.Version?.ToString() ?? "1.0.0";
            Version latestStableVersion = null;

            foreach (var elem in root.EnumerateArray())
            {
                if (elem.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean())
                {
                    continue;
                }

                var tagName = elem.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                var cleanVersion = tagName.TrimStart('v', 'V');
                var isPrerelease = elem.TryGetProperty("prerelease", out var prereleaseProp) && prereleaseProp.GetBoolean();

                if (!isPrerelease && Version.TryParse(cleanVersion, out var parsedVersion))
                {
                    if (latestStableVersion == null || parsedVersion > latestStableVersion)
                    {
                        latestStableVersion = parsedVersion;
                    }
                }
                var publishedAt = elem.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt)
                    ? dt
                    : (elem.TryGetProperty("created_at", out var createProp) && createProp.TryGetDateTime(out var cdt) ? cdt : DateTime.UtcNow);

                var htmlUrl = elem.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
                var body = elem.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : string.Empty;

                var platformIdentifier = GetPackagePlatformIdentifier();
                var packageExtension = GetPackageExtension();
                var fileName = GetPackageFileName(cleanVersion);
                string downloadUrl = null;

                if (elem.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    var assetNames = new List<string>();
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameProp))
                        {
                            var nameStr = nameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(nameStr))
                            {
                                assetNames.Add(nameStr);
                            }
                        }
                    }

                    fileName = SelectPackageAssetFileName(assetNames, platformIdentifier, packageExtension, fileName);

                    var expectedSuffix = platformIdentifier + packageExtension;
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        if (!asset.TryGetProperty("name", out var nameProp))
                        {
                            continue;
                        }

                        var assetName = nameProp.GetString();
                        if (string.IsNullOrWhiteSpace(assetName) ||
                            !assetName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (asset.TryGetProperty("browser_download_url", out var downloadProp))
                        {
                            downloadUrl = downloadProp.GetString();
                        }

                        break;
                    }
                }

                downloadUrl ??= BuildGitHubAssetDownloadUrl(tagName, fileName);

                var isInstalled = string.Equals(cleanVersion, currentVer, StringComparison.OrdinalIgnoreCase) ||
                                    cleanVersion.StartsWith(currentVer, StringComparison.OrdinalIgnoreCase);

                result.Add(new UpdatePackage
                {
                    Version = cleanVersion,
                    ReleaseDate = publishedAt,
                    FileName = fileName,
                    Url = htmlUrl ?? "https://github.com/dmzoneill/Leecharr/releases",
                    DownloadUrl = downloadUrl,
                    Installed = isInstalled,
                    Latest = false,
                    Changes = ParseChanges(body),
                });
            }

            if (latestStableVersion != null)
            {
                foreach (var package in result)
                {
                    if (Version.TryParse(package.Version, out var packageVersion) && packageVersion == latestStableVersion)
                    {
                        package.Latest = true;
                        break;
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to fetch updates from GitHub releases");
            return null;
        }
    }

    private static UpdatePackageChanges ParseChanges(string body)
    {
        var changes = new UpdatePackageChanges();
        if (string.IsNullOrWhiteSpace(body))
        {
            return changes;
        }

        var lines = body.Split(NewlineSeparators, StringSplitOptions.RemoveEmptyEntries);
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
