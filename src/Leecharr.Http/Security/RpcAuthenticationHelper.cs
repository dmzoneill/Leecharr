// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public static class RpcAuthenticationHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }

    public static bool IsAuthenticated(HttpContext context, IConfigFileProvider configFileProvider)
    {
        if (context == null)
        {
            return false;
        }

        configFileProvider ??= context.RequestServices?.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider;

        // 0. When authentication is disabled, automatically grant access
        if (configFileProvider != null && !configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        // 1. User principal already authenticated by ASP.NET Core (e.g. SmartAuth, Cookies, Identity)
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        var masterApiKey = configFileProvider?.ApiKey;

        // 2. Check X-Api-Key or ApiKey header
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(headerKey.ToString(), masterApiKey))
            {
                return true;
            }
        }

        if (context.Request.Headers.TryGetValue("ApiKey", out var customApiKey) && !string.IsNullOrWhiteSpace(customApiKey))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(customApiKey.ToString(), masterApiKey))
            {
                return true;
            }
        }

        // 3. Check query parameters: apikey or api_key or token or access_token (legacy compatibility with warning)
        if (context.Request.Query.TryGetValue("apikey", out var queryApiKey) && !string.IsNullOrWhiteSpace(queryApiKey))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(queryApiKey.ToString(), masterApiKey))
            {
                Logger.Warn("API key supplied via URL query parameter on {0}. Please use X-Api-Key header or Authorization: Bearer token instead.", context.Request?.Path.Value);
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("access_token", out var queryAccessToken) && !string.IsNullOrWhiteSpace(queryAccessToken))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(queryAccessToken.ToString(), masterApiKey))
            {
                Logger.Warn("API key supplied via URL query parameter on {0}. Please use X-Api-Key header or Authorization: Bearer token instead.", context.Request?.Path.Value);
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("api_key", out var queryApiKey2) && !string.IsNullOrWhiteSpace(queryApiKey2))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(queryApiKey2.ToString(), masterApiKey))
            {
                Logger.Warn("API key supplied via URL query parameter on {0}. Please use X-Api-Key header or Authorization: Bearer token instead.", context.Request?.Path.Value);
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("token", out var queryToken) && !string.IsNullOrWhiteSpace(queryToken))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(queryToken.ToString(), masterApiKey))
            {
                Logger.Warn("API key supplied via URL query parameter on {0}. Please use X-Api-Key header or Authorization: Bearer token instead.", context.Request?.Path.Value);
                return true;
            }
        }

        // 4. Check HTTP Basic Auth (Authorization: Basic ...) - only as password
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeaderVal) && !string.IsNullOrWhiteSpace(authHeaderVal))
        {
            var authHeader = authHeaderVal.ToString();
            if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var creds = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader["Basic ".Length..].Trim()));
                    var parts = creds.Split(':', 2);
                    var password = parts.Length > 1 ? parts[1] : string.Empty;

                    if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(password, masterApiKey))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Invalid base64, fall through
                }
            }
            else if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(token, masterApiKey))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
