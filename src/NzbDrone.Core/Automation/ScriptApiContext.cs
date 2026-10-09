#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Automation;

#pragma warning disable SA1300 // Element should begin with upper-case letter (DSL wrapper)
using System.Threading.Tasks;

public class ScriptApiContext : IDisposable
{
    private readonly IConfigFileProvider? _configFileProvider;
    private readonly ScriptHttpContext _http;
    private readonly bool _disposeHttp;

    public ScriptApiContext(IConfigFileProvider? configFileProvider = null, ScriptHttpContext? http = null)
    {
        this._configFileProvider = configFileProvider;
        this._http = http ?? new ScriptHttpContext();
        this._disposeHttp = http == null;
    }

    public object get(string path, object? options = null)
    {
        var url = BuildApiUrl(path);
        var opts = AttachAuthHeaders(options);
        return _http.get(url, opts);
    }

    public object post(string path, object? body = null, object? options = null)
    {
        var url = BuildApiUrl(path);
        var opts = AttachAuthHeaders(options);
        return _http.post(url, body, opts);
    }

    public object put(string path, object? body = null, object? options = null)
    {
        var url = BuildApiUrl(path);
        var opts = AttachAuthHeaders(options);
        return _http.put(url, body, opts);
    }

    public object delete(string path, object? options = null)
    {
        var url = this.BuildApiUrl(path);
        var opts = this.AttachAuthHeaders(options);
        return this._http.delete(url, opts);
    }

    public Task<Dictionary<string, object?>> getAsync(string path, object? options = null)
    {
        var url = this.BuildApiUrl(path);
        var opts = this.AttachAuthHeaders(options);
        return this._http.getAsync(url, opts);
    }

    public Task<Dictionary<string, object?>> postAsync(string path, object? body = null, object? options = null)
    {
        var url = this.BuildApiUrl(path);
        var opts = this.AttachAuthHeaders(options);
        return this._http.postAsync(url, body, opts);
    }

    public Task<Dictionary<string, object?>> putAsync(string path, object? body = null, object? options = null)
    {
        var url = this.BuildApiUrl(path);
        var opts = this.AttachAuthHeaders(options);
        return this._http.putAsync(url, body, opts);
    }

    public Task<Dictionary<string, object?>> deleteAsync(string path, object? options = null)
    {
        var url = this.BuildApiUrl(path);
        var opts = this.AttachAuthHeaders(options);
        return this._http.deleteAsync(url, opts);
    }

    public void Dispose()
    {
        if (this._disposeHttp)
        {
            this._http.Dispose();
        }
    }

    private string BuildApiUrl(string path)
    {
        var enableSsl = _configFileProvider?.EnableSsl == true;
        var scheme = enableSsl ? "https" : "http";
        var port = enableSsl
            ? _configFileProvider?.SslPort ?? 7890
            : _configFileProvider?.Port ?? 7889;
        var urlBase = NormalizeUrlBase(_configFileProvider?.UrlBase);
        var cleanPath = path?.TrimStart('/') ?? string.Empty;

        if (cleanPath.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            cleanPath = cleanPath.Substring(7);
        }

        var host = ResolveLocalApiHost(_configFileProvider?.BindAddress);
        var authority = FormatHostWithPort(host, port);

        return $"{scheme}://{authority}{urlBase}/api/v1/{cleanPath}";
    }

    private static string ResolveLocalApiHost(string? bindAddress)
    {
        var trimmed = bindAddress?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed is "*" or "0.0.0.0" or "::" or "+")
        {
            return "127.0.0.1";
        }

        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        return trimmed;
    }

    private static string NormalizeUrlBase(string? urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return string.Empty;
        }

        var trimmed = urlBase.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : "/" + trimmed;
    }

    private static string FormatHostWithPort(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var trimmedHost = host.Trim().TrimEnd('/');

        if (IPAddress.TryParse(trimmedHost, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            trimmedHost = $"[{ip}]";
        }

        var hasPort = trimmedHost.StartsWith('[') && trimmedHost.Contains(']')
            ? trimmedHost[(trimmedHost.IndexOf(']') + 1)..].Contains(':')
            : trimmedHost.Contains(':');

        if (hasPort || port is 80 or 443)
        {
            return trimmedHost;
        }

        return $"{trimmedHost}:{port}";
    }

    private IDictionary<string, object> AttachAuthHeaders(object? options)
    {
        var opts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (options is IDictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                opts[kvp.Key] = kvp.Value;
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (opts.TryGetValue("headers", out var existingHeaders) && existingHeaders is IDictionary<string, string> hDict)
        {
            foreach (var kvp in hDict)
            {
                headers[kvp.Key] = kvp.Value;
            }
        }

        var apiKey = _configFileProvider?.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers["X-Api-Key"] = apiKey;
        }

        if (_configFileProvider?.EnableSsl == true)
        {
            opts["allowInsecureTls"] = true;
        }

        opts["headers"] = headers;
        return opts;
    }
}
#pragma warning restore SA1300
