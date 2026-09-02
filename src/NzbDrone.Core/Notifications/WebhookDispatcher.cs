using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Notifications;

public interface IWebhookDispatcher
{
    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null);
}

public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly Logger _logger;

    public WebhookDispatcher()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, null)
    {
    }

    public WebhookDispatcher(HttpClient httpClient)
        : this(httpClient, null)
    {
    }

    internal WebhookDispatcher(HttpClient httpClient, AsyncRetryPolicy<HttpResponseMessage> retryPolicy)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _logger = LogManager.GetCurrentClassLogger();

        _retryPolicy = retryPolicy ?? Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2s, 4s, 8s
                (outcome, timespan, retryAttempt, context) =>
                {
                    _logger.Warn("Webhook dispatch failed. Retrying in {0}s (Attempt {1}/3)...", timespan.TotalSeconds, retryAttempt);
                });
    }

    public async Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var response = await _retryPolicy.ExecuteAsync(async () =>
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                {
                    Content = content
                };

                if (!string.IsNullOrWhiteSpace(customHeadersJson))
                {
                    try
                    {
                        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson);
                        if (headers != null)
                        {
                            foreach (var kvp in headers)
                            {
                                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore header parse error
                    }
                }

                return await _httpClient.SendAsync(request);
            });

            if (response.IsSuccessStatusCode)
            {
                _logger.Info("Webhook successfully dispatched to {0} (Status: {1})", targetUrl, response.StatusCode);
                return true;
            }

            _logger.Warn("Webhook dispatch to {0} returned non-success status code: {1}", targetUrl, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to dispatch webhook to {0}", targetUrl);
            return false;
        }
    }
}
