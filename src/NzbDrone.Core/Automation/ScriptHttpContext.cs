#nullable enable
#pragma warning disable SA1300

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Automation;

public class ScriptHttpContext : IDisposable
{
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly HttpClient _client;
    private readonly bool _disposeClient;
    private HttpClient? _insecureClient;
    private bool _disposed;

    public ScriptHttpContext()
        : this(null, null, false)
    {
    }

    public ScriptHttpContext(CookieContainer? cookieContainer)
        : this(null, cookieContainer, false)
    {
    }

    public ScriptHttpContext(bool allowInsecureTls)
        : this(null, null, allowInsecureTls)
    {
    }

    public ScriptHttpContext(HttpMessageHandler handler, CookieContainer? cookieContainer = null)
    {
        this.CookieContainer = cookieContainer ?? new CookieContainer();
        this.AllowInsecureTls = false;
        this._client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        this._client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        this._disposeClient = true;
    }

    public ScriptHttpContext(HttpClient? client, CookieContainer? cookieContainer = null, bool allowInsecureTls = false)
    {
        this.CookieContainer = cookieContainer ?? new CookieContainer();
        this.AllowInsecureTls = allowInsecureTls;

        if (client != null)
        {
            this._client = client;
            this._disposeClient = false;
        }
        else
        {
            this._client = CreateHttpClient(this.CookieContainer, allowInsecureTls);
            this._disposeClient = true;
        }
    }

    public CookieContainer CookieContainer { get; }

    public bool AllowInsecureTls { get; }

    public static HttpClientHandler CreateHandler(CookieContainer cookieContainer, bool allowInsecureTls = false)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = cookieContainer,
        };

        if (allowInsecureTls)
        {
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
        }

        return handler;
    }

    public static HttpClient CreateHttpClient(CookieContainer cookieContainer, bool allowInsecureTls = false)
    {
        var handler = CreateHandler(cookieContainer, allowInsecureTls);
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        return client;
    }

    public object get(string url, IDictionary<string, object>? options = null) => this.Get(url, options);

    public object post(string url, object? body = null, IDictionary<string, object>? options = null) => this.Post(url, body, options);

    public object put(string url, object? body = null, IDictionary<string, object>? options = null) => this.Put(url, body, options);

    public object delete(string url, IDictionary<string, object>? options = null) => this.Delete(url, options);

    public object Get(string url, IDictionary<string, object>? options = null)
    {
        return Task.Run(() => this.GetAsync(url, options)).GetAwaiter().GetResult();
    }

    public object Post(string url, object? body = null, IDictionary<string, object>? options = null)
    {
        return Task.Run(() => this.PostAsync(url, body, options)).GetAwaiter().GetResult();
    }

    public object Put(string url, object? body = null, IDictionary<string, object>? options = null)
    {
        return Task.Run(() => this.PutAsync(url, body, options)).GetAwaiter().GetResult();
    }

    public object Delete(string url, IDictionary<string, object>? options = null)
    {
        return Task.Run(() => this.DeleteAsync(url, options)).GetAwaiter().GetResult();
    }

    public Task<Dictionary<string, object?>> getAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.GetAsync(url, options, cancellationToken);

    public Task<Dictionary<string, object?>> postAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.PostAsync(url, body, options, cancellationToken);

    public Task<Dictionary<string, object?>> putAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.PutAsync(url, body, options, cancellationToken);

    public Task<Dictionary<string, object?>> deleteAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.DeleteAsync(url, options, cancellationToken);

    public Task<Dictionary<string, object?>> GetAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return this.SendAsync(HttpMethod.Get, url, null, options, cancellationToken);
    }

    public Task<Dictionary<string, object?>> PostAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return this.SendAsync(HttpMethod.Post, url, body, options, cancellationToken);
    }

    public Task<Dictionary<string, object?>> PutAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return this.SendAsync(HttpMethod.Put, url, body, options, cancellationToken);
    }

    public Task<Dictionary<string, object?>> DeleteAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return this.SendAsync(HttpMethod.Delete, url, null, options, cancellationToken);
    }

    public void Dispose()
    {
        if (!this._disposed)
        {
            this._disposed = true;
            if (this._disposeClient)
            {
                this._client.Dispose();
            }

            this._insecureClient?.Dispose();
        }
    }

    private async Task<Dictionary<string, object?>> SendAsync(
        HttpMethod method,
        string url,
        object? body,
        IDictionary<string, object>? options,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url);

        if (options != null)
        {
            if (options.TryGetValue("headers", out var rawHeaders) && rawHeaders is IDictionary<string, object> headersDict)
            {
                foreach (var kvp in headersDict)
                {
                    request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value?.ToString());
                }
            }

            if (options.TryGetValue("cookies", out var rawCookies))
            {
                if (rawCookies is IDictionary<string, object> cookiesDict)
                {
                    var cookieHeader = new StringBuilder();
                    foreach (var kvp in cookiesDict)
                    {
                        if (cookieHeader.Length > 0)
                        {
                            cookieHeader.Append("; ");
                        }

                        cookieHeader.Append($"{kvp.Key}={kvp.Value}");
                    }

                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader.ToString());
                }
                else if (rawCookies is string cookieStr)
                {
                    request.Headers.TryAddWithoutValidation("Cookie", cookieStr);
                }
            }
        }

        if (body != null)
        {
            if (body is string strBody)
            {
                var contentType = "text/plain";
                if (options != null && options.TryGetValue("contentType", out var ct) && ct != null)
                {
                    contentType = ct.ToString()!;
                }
                else if (strBody.TrimStart().StartsWith('{') || strBody.TrimStart().StartsWith('['))
                {
                    contentType = "application/json";
                }

                request.Content = new StringContent(strBody, Encoding.UTF8, contentType);
            }
            else if (body is IDictionary<string, object> formDict)
            {
                var isJson = options != null && options.TryGetValue("json", out var jsonOpt) && jsonOpt is true;
                if (isJson)
                {
                    var jsonStr = JsonSerializer.Serialize(formDict);
                    request.Content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                }
                else
                {
                    var formList = new List<KeyValuePair<string, string>>();
                    foreach (var kvp in formDict)
                    {
                        formList.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
                    }

                    request.Content = new FormUrlEncodedContent(formList);
                }
            }
        }

        var timeoutSeconds = 15;
        if (options != null && options.TryGetValue("timeout", out var timeoutVal) && timeoutVal != null)
        {
            if (int.TryParse(timeoutVal.ToString(), out var parsedTimeout) && parsedTimeout > 0)
            {
                timeoutSeconds = Math.Min(parsedTimeout, 60);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var client = this.GetClientForRequest(options);
        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        var isOk = response.IsSuccessStatusCode;

        object? parsedJson = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(responseBody) &&
                (responseBody.TrimStart().StartsWith('{') || responseBody.TrimStart().StartsWith('[')))
            {
                using var doc = JsonDocument.Parse(responseBody);
                parsedJson = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            }
        }
        catch
        {
            // not valid JSON
        }

        var resHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
        {
            resHeaders[h.Key] = string.Join(", ", h.Value);
        }

        if (response.Content?.Headers != null)
        {
            foreach (var h in response.Content.Headers)
            {
                resHeaders[h.Key] = string.Join(", ", h.Value);
            }
        }

        return new Dictionary<string, object?>
        {
            ["status"] = statusCode,
            ["ok"] = isOk,
            ["body"] = responseBody,
            ["json"] = parsedJson,
            ["headers"] = resHeaders,
        };
    }

    private HttpClient GetClientForRequest(IDictionary<string, object>? options)
    {
        var requestInsecure = this.AllowInsecureTls;
        if (options != null)
        {
            if (options.TryGetValue("allowInsecureTls", out var optInsecure) && optInsecure is bool b1)
            {
                requestInsecure = b1;
            }
            else if (options.TryGetValue("insecure", out var optInsec2) && optInsec2 is bool b2)
            {
                requestInsecure = b2;
            }
            else if (options.TryGetValue("rejectUnauthorized", out var optReject) && optReject is bool b3)
            {
                requestInsecure = !b3;
            }
        }

        if (requestInsecure)
        {
            if (this.AllowInsecureTls)
            {
                return this._client;
            }

            if (this._insecureClient == null)
            {
                this._insecureClient = CreateHttpClient(this.CookieContainer, allowInsecureTls: true);
            }

            return this._insecureClient;
        }

        return this._client;
    }
}
#pragma warning restore SA1300
