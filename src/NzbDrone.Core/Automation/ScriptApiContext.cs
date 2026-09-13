#nullable enable
using System;
using System.Collections.Generic;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Automation;

#pragma warning disable SA1300 // Element should begin with upper-case letter (DSL wrapper)
public class ScriptApiContext
{
    private readonly IConfigFileProvider? _configFileProvider;
    private readonly ScriptHttpContext _http;

    public ScriptApiContext(IConfigFileProvider? configFileProvider = null, ScriptHttpContext? http = null)
    {
        _configFileProvider = configFileProvider;
        _http = http ?? new ScriptHttpContext();
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
        var url = BuildApiUrl(path);
        var opts = AttachAuthHeaders(options);
        return _http.delete(url, opts);
    }

    private string BuildApiUrl(string path)
    {
        var port = _configFileProvider?.Port ?? 8989;
        var urlBase = _configFileProvider?.UrlBase?.TrimEnd('/') ?? string.Empty;
        var cleanPath = path?.TrimStart('/') ?? string.Empty;

        if (cleanPath.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            cleanPath = cleanPath.Substring(7);
        }

        return $"http://127.0.0.1:{port}{urlBase}/api/v1/{cleanPath}";
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

        opts["headers"] = headers;
        return opts;
    }
}
#pragma warning restore SA1300
