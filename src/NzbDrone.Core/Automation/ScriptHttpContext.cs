#nullable enable
#pragma warning disable SA1300

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Automation;

public class ScriptHttpContext : IDisposable
{
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private const int ResponseReadBufferSize = 8192;

    private readonly HttpClient _client;
    private readonly bool _disposeClient;
    private readonly ScriptExecutionBudget? _executionBudget;
    private HttpClient? _insecureClient;
    private bool _disposed;
    private int _pendingOperations;

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

    public ScriptHttpContext(HttpMessageHandler handler, CookieContainer? cookieContainer = null, ScriptExecutionBudget? executionBudget = null)
    {
        this.CookieContainer = cookieContainer ?? new CookieContainer();
        this.AllowInsecureTls = false;
        this._executionBudget = executionBudget;
        this._client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        this._client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        this._disposeClient = true;
    }

    public ScriptHttpContext(HttpClient? client, CookieContainer? cookieContainer = null, bool allowInsecureTls = false, ScriptExecutionBudget? executionBudget = null)
    {
        this.CookieContainer = cookieContainer ?? new CookieContainer();
        this.AllowInsecureTls = allowInsecureTls;
        this._executionBudget = executionBudget;

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
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == SslPolicyErrors.None)
                {
                    return true;
                }

                return allowInsecureTls;
            };
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
        return this.Send(HttpMethod.Get, url, null, options, CancellationToken.None);
    }

    public object Post(string url, object? body = null, IDictionary<string, object>? options = null)
    {
        return this.Send(HttpMethod.Post, url, body, options, CancellationToken.None);
    }

    public object Put(string url, object? body = null, IDictionary<string, object>? options = null)
    {
        return this.Send(HttpMethod.Put, url, body, options, CancellationToken.None);
    }

    public object Delete(string url, IDictionary<string, object>? options = null)
    {
        return this.Send(HttpMethod.Delete, url, null, options, CancellationToken.None);
    }

    public Task<Dictionary<string, object?>> getAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.GetAsync(url, options, cancellationToken);

    public Task<Dictionary<string, object?>> postAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.PostAsync(url, body, options, cancellationToken);

    public Task<Dictionary<string, object?>> putAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.PutAsync(url, body, options, cancellationToken);

    public Task<Dictionary<string, object?>> deleteAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default) => this.DeleteAsync(url, options, cancellationToken);

    public Task<Dictionary<string, object?>> GetAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.Send(HttpMethod.Get, url, null, options, cancellationToken));
    }

    public Task<Dictionary<string, object?>> PostAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.Send(HttpMethod.Post, url, body, options, cancellationToken));
    }

    public Task<Dictionary<string, object?>> PutAsync(string url, object? body = null, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.Send(HttpMethod.Put, url, body, options, cancellationToken));
    }

    public Task<Dictionary<string, object?>> DeleteAsync(string url, IDictionary<string, object>? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.Send(HttpMethod.Delete, url, null, options, cancellationToken));
    }

    public void WaitForPendingOperations(TimeSpan maxWait)
    {
        if (maxWait <= TimeSpan.Zero)
        {
            return;
        }

        var deadline = Environment.TickCount64 + (long)maxWait.TotalMilliseconds;
        while (Volatile.Read(ref this._pendingOperations) > 0 && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        if (!this._disposed)
        {
            this._disposed = true;
            this.WaitForPendingOperations(TimeSpan.FromSeconds(60));
            if (this._disposeClient)
            {
                this._client.Dispose();
            }

            this._insecureClient?.Dispose();
        }
    }

    private Dictionary<string, object?> Send(
        HttpMethod method,
        string url,
        object? body,
        IDictionary<string, object>? options,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref this._pendingOperations);
        try
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

                request.Content = CreateStringContent(strBody, contentType);
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

        var timeoutMs = timeoutSeconds * 1000;
        if (this._executionBudget != null)
        {
            timeoutMs = this._executionBudget.CapTimeoutMilliseconds(timeoutSeconds);
            if (timeoutMs <= 0)
            {
                throw new OperationCanceledException("Script execution time budget exhausted before HTTP request could start.");
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var client = this.GetClientForRequest(options);
        using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        var responseBody = ReadResponseBodyAsString(response.Content, cts.Token);
        var statusCode = (int)response.StatusCode;
        var isOk = response.IsSuccessStatusCode;

        object? parsedJson = null;
        try
        {
            parsedJson = ScriptJsonElementConverter.TryParse(responseBody);
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
        finally
        {
            Interlocked.Decrement(ref this._pendingOperations);
        }
    }

    private static string ReadResponseBodyAsString(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content == null)
        {
            return string.Empty;
        }

        if (content.Headers.ContentLength is > ScriptMemoryLimits.MaxBytes)
        {
            throw new ScriptMemoryLimitExceededException();
        }

        using var stream = content.ReadAsStream(cancellationToken);
        using var bufferStream = new MemoryStream();
        var buffer = new byte[ResponseReadBufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (bufferStream.Length + read > ScriptMemoryLimits.MaxBytes)
            {
                throw new ScriptMemoryLimitExceededException();
            }

            bufferStream.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(bufferStream.ToArray());
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

    private static StringContent CreateStringContent(string body, string contentType)
    {
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            var content = new StringContent(body, Encoding.UTF8, mediaType.MediaType);
            content.Headers.ContentType = mediaType;
            return content;
        }

        return new StringContent(body, Encoding.UTF8, contentType);
    }
}
#pragma warning restore SA1300
