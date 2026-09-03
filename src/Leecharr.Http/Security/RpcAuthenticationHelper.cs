// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text;
using Microsoft.AspNetCore.Http;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public static class RpcAuthenticationHelper
{
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
            if (!string.IsNullOrWhiteSpace(masterApiKey) && string.Equals(headerKey.ToString(), masterApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (context.Request.Headers.TryGetValue("ApiKey", out var customApiKey) && !string.IsNullOrWhiteSpace(customApiKey))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && string.Equals(customApiKey.ToString(), masterApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // 3. Check query parameters: apikey or api_key or token
        if (context.Request.Query.TryGetValue("apikey", out var queryApiKey) && !string.IsNullOrWhiteSpace(queryApiKey))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && string.Equals(queryApiKey.ToString(), masterApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("api_key", out var queryApiKey2) && !string.IsNullOrWhiteSpace(queryApiKey2))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && string.Equals(queryApiKey2.ToString(), masterApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("token", out var queryToken) && !string.IsNullOrWhiteSpace(queryToken))
        {
            if (!string.IsNullOrWhiteSpace(masterApiKey) && string.Equals(queryToken.ToString(), masterApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // 4. Check HTTP Basic Auth (Authorization: Basic ...)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeaderVal) && !string.IsNullOrWhiteSpace(authHeaderVal))
        {
            var authHeader = authHeaderVal.ToString();
            if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var creds = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader["Basic ".Length..].Trim()));
                    var parts = creds.Split(':', 2);
                    var username = parts[0];
                    var password = parts.Length > 1 ? parts[1] : string.Empty;

                    if (!string.IsNullOrWhiteSpace(masterApiKey) &&
                        (string.Equals(password, masterApiKey, StringComparison.Ordinal) ||
                         string.Equals(username, masterApiKey, StringComparison.Ordinal)))
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
                if (!string.IsNullOrWhiteSpace(masterApiKey) && string.Equals(token, masterApiKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
