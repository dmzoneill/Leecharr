// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Api.V1.ArrIntegration;

[V1ApiController("arrconnections")]
[Route("api/v1/arrconnection")]
[Authorize(Policy = "RequireOperator")]
public class ArrConnectionController : Controller
{
    private static readonly HttpClient DefaultHttpClient = new(new SocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly IArrConnectionRepository repository;
    private readonly HttpClient httpClient;

    public ArrConnectionController(IArrConnectionRepository repository, HttpClient httpClient = null)
    {
        this.repository = repository;
        this.httpClient = httpClient;
    }

    [HttpGet]
    public ActionResult<List<ArrConnectionResource>> GetAll()
    {
        var definitions = this.repository.All();
        return this.Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<ArrConnectionResource> Get(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<ArrConnectionResource> Create([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Connection name is required.");
        }

        if (!string.IsNullOrWhiteSpace(resource.Url) && !IsValidTargetUrl(resource.Url, out var error))
        {
            return this.BadRequest(error);
        }

        var model = ToModel(resource);
        var created = this.repository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ArrConnectionResource> Update(int id, [FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Connection name is required.");
        }

        if (!string.IsNullOrWhiteSpace(resource.Url) && !IsValidTargetUrl(resource.Url, out var error))
        {
            return this.BadRequest(error);
        }

        var existing = this.repository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        if (model.ApiKey == "********" || (model.ApiKey != null && model.ApiKey.Contains('*')) || string.IsNullOrWhiteSpace(model.ApiKey))
        {
            model.ApiKey = existing.ApiKey;
        }

        this.repository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.repository.Delete(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<ArrTestResult>> Test(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return await this.TestDirectInternal(ToResource(definition));
    }

    [HttpPost("test")]
    public async Task<ActionResult<ArrTestResult>> TestDirect([FromBody] ArrConnectionResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        if (resource.Id > 0 && (resource.ApiKey == "********" || (resource.ApiKey != null && resource.ApiKey.Contains('*')) || string.IsNullOrWhiteSpace(resource.ApiKey)))
        {
            var existing = this.repository.Get(resource.Id);
            if (existing != null)
            {
                resource.ApiKey = existing.ApiKey;
            }
        }

        return await this.TestDirectInternal(resource);
    }

    public static string ResolveExternalUrl(string configuredExternalUrl, string arrType, string name)
    {
        if (!string.IsNullOrWhiteSpace(configuredExternalUrl))
        {
            return configuredExternalUrl.Trim();
        }

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var cleanName = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]", "_").ToUpperInvariant();
            candidates.Add($"LEECHARR__ARR_{cleanName}_PUBLIC_URL");
            candidates.Add($"LEECHARR__ARR_{cleanName}_EXTERNAL_URL");
            candidates.Add($"LEECHARR__{cleanName}_PUBLIC_URL");
            candidates.Add($"LEECHARR__{cleanName}_EXTERNAL_URL");
            candidates.Add($"{cleanName}_PUBLIC_URL");
            candidates.Add($"{cleanName}_EXTERNAL_URL");
        }

        if (!string.IsNullOrWhiteSpace(arrType))
        {
            var cleanType = Regex.Replace(arrType.Trim(), @"[^a-zA-Z0-9_]", "_").ToUpperInvariant();
            candidates.Add($"LEECHARR__ARR_{cleanType}_PUBLIC_URL");
            candidates.Add($"LEECHARR__ARR_{cleanType}_EXTERNAL_URL");
            candidates.Add($"LEECHARR__{cleanType}_PUBLIC_URL");
            candidates.Add($"LEECHARR__{cleanType}_EXTERNAL_URL");
            candidates.Add($"{cleanType}_PUBLIC_URL");
            candidates.Add($"{cleanType}_EXTERNAL_URL");
        }

        candidates.Add("LEECHARR__ARR_PUBLIC_URL");
        candidates.Add("LEECHARR__ARR_EXTERNAL_URL");

        foreach (var key in candidates)
        {
            var val = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val.Trim();
            }
        }

        return null;
    }

    private static ArrConnectionResource ToResource(ArrConnectionDefinition model)
    {
        return new ArrConnectionResource
        {
            Id = model.Id,
            Name = model.Name,
            ArrType = model.ArrType,
            Url = model.Url,
            ExternalUrl = ResolveExternalUrl(model.ExternalUrl, model.ArrType, model.Name),
            ApiKey = string.IsNullOrEmpty(model.ApiKey) ? string.Empty : "********",
            Enabled = model.Enable,
            SyncCategories = model.SyncCategories,
            RefreshIntervalMinutes = model.SyncIntervalMinutes,
        };
    }

    private static ArrConnectionDefinition ToModel(ArrConnectionResource resource)
    {
        return new ArrConnectionDefinition
        {
            Id = resource.Id,
            Name = resource.Name,
            ArrType = resource.ArrType ?? "Sonarr",
            Implementation = resource.ArrType ?? "Sonarr",
            Url = resource.Url,
            ExternalUrl = resource.ExternalUrl,
            ApiKey = resource.ApiKey,
            Enable = resource.Enabled,
            SyncCategories = resource.SyncCategories,
            SyncIntervalMinutes = resource.RefreshIntervalMinutes > 0 ? resource.RefreshIntervalMinutes : 15,
            SyncEnabled = resource.Enabled,
        };
    }

    public static bool IsValidTargetUrl(string targetUrl, out string errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            errorMessage = "URL cannot be empty.";
            return false;
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            errorMessage = $"Invalid URL format: '{targetUrl}'.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Unsupported URL scheme '{uri.Scheme}'. Only HTTP and HTTPS schemes are allowed.";
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            errorMessage = "URL host cannot be empty.";
            return false;
        }

        if (string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".instance-data", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Access to metadata host '{host}' is prohibited.";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsBlockedIp(ip))
            {
                errorMessage = $"Access to prohibited IP address '{ip}' is blocked.";
                return false;
            }
        }
        else
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                if (addresses != null && addresses.Length > 0)
                {
                    foreach (var addr in addresses)
                    {
                        if (IsBlockedIp(addr))
                        {
                            errorMessage = $"Host '{host}' resolves to prohibited IP address '{addr}'.";
                            return false;
                        }
                    }
                }
            }
            catch (SocketException)
            {
                // DNS resolution failure (e.g. offline or mock host)
            }
        }

        return true;
    }

    public static bool IsBlockedIp(IPAddress ip)
    {
        if (ip == null)
        {
            return true;
        }

        if (ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.Broadcast) ||
            ip.Equals(IPAddress.None) ||
            ip.Equals(IPAddress.IPv6Any) ||
            ip.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 0 ||
                (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) ||
                (bytes[0] == 169 && bytes[1] == 254))
            {
                return true;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ActionResult<ArrTestResult>> TestDirectInternal(ArrConnectionResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return this.Ok(new ArrTestResult { Success = false, Message = "URL is required." });
        }

        if (!IsValidTargetUrl(resource.Url, out var ssrfError))
        {
            return this.BadRequest(ssrfError);
        }

        var client = this.httpClient ?? DefaultHttpClient;
        var baseUrl = resource.Url.TrimEnd('/');
        var endpoints = new[] { "/api/v3/system/status", "/api/v1/system/status" };
        string lastError = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + endpoint);
                if (!string.IsNullOrWhiteSpace(resource.ApiKey))
                {
                    req.Headers.Add("X-Api-Key", resource.ApiKey);
                }

                using var resp = await client.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var version = endpoint.Contains("v3") ? "v3" : "v1";
                    return this.Ok(new ArrTestResult
                    {
                        Success = true,
                        Message = $"Connected to {resource.ArrType ?? "Arr"} successfully.",
                        Version = version,
                    });
                }

                lastError = $"Server returned HTTP {(int)resp.StatusCode} {resp.StatusCode}.";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return this.Ok(new ArrTestResult
        {
            Success = false,
            Message = lastError ?? $"Failed to connect to {resource.ArrType ?? "Arr"}.",
        });
    }
}
