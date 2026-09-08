// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http.Transport;

public static class AntiBotChallengeDetector
{
    private static readonly string[] ChallengeSignatures = new[]
    {
        "Just a moment...",
        "Attention Required! | Cloudflare",
        "Security Check | DDoS-GUARD",
        "cf-browser-verification",
        "cf-turnstile",
        "turnstile",
        "challenges.cloudflare.com",
        "_cf_chl_opt",
        "_cf_chl_ctx",
        "cloudflare-nginx",
        "ddos-guard",
        "Checking your browser before accessing",
        "Checking your browser",
        "ray id",
        "cf_clearance",
    };

    public static bool IsChallenge(HttpStatusCode statusCode, string bodyHtml, HttpResponseMessage response = null)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            if (response != null && (statusCode == HttpStatusCode.Forbidden || statusCode == HttpStatusCode.ServiceUnavailable || (int)statusCode == 429))
            {
                if (response.Headers.Contains("cf-mitigated") ||
                    response.Headers.Contains("cf-ray") ||
                    (response.Headers.Server != null && response.Headers.Server.ToString().Contains("cloudflare", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var signature in ChallengeSignatures)
        {
            if (bodyHtml.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (response != null && response.Headers.Contains("cf-mitigated"))
        {
            var values = response.Headers.GetValues("cf-mitigated");
            if (values.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsChallenge(string bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            return false;
        }

        foreach (var signature in ChallengeSignatures)
        {
            if (bodyHtml.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<bool> IsChallengeAsync(HttpResponseMessage response)
    {
        if (response == null)
        {
            return false;
        }

        var isPotentialChallengeCode = response.StatusCode == HttpStatusCode.Forbidden
                                    || response.StatusCode == HttpStatusCode.ServiceUnavailable
                                    || (int)response.StatusCode == 429
                                    || response.StatusCode == HttpStatusCode.OK;

        var hasCfHeaders = response.Headers.Contains("cf-mitigated")
                        || response.Headers.Contains("cf-ray")
                        || (response.Headers.Server != null && response.Headers.Server.ToString().Contains("cloudflare", StringComparison.OrdinalIgnoreCase));

        if (!isPotentialChallengeCode && !hasCfHeaders)
        {
            return false;
        }

        if (response.Headers.Contains("cf-mitigated"))
        {
            var values = response.Headers.GetValues("cf-mitigated");
            if (values.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (response.Content == null)
        {
            return (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable) && hasCfHeaders;
        }

        try
        {
            await response.Content.LoadIntoBufferAsync();
            var body = await response.Content.ReadAsStringAsync();
            return IsChallenge(response.StatusCode, body, response);
        }
        catch
        {
            return false;
        }
    }
}
