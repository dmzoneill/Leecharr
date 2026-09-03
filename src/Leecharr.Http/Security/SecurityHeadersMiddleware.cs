// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Leecharr.Http.Security;

public class SecurityHeadersMiddleware
{
    public const string XFrameOptionsHeader = "X-Frame-Options";
    public const string XFrameOptionsValue = "SAMEORIGIN";

    public const string XContentTypeOptionsHeader = "X-Content-Type-Options";
    public const string XContentTypeOptionsValue = "nosniff";

    public const string ReferrerPolicyHeader = "Referrer-Policy";
    public const string ReferrerPolicyValue = "strict-origin-when-cross-origin";

    public const string PermissionsPolicyHeader = "Permissions-Policy";
    public const string PermissionsPolicyValue = "geolocation=(), camera=(), microphone=(), payment=()";

    public const string ContentSecurityPolicyHeader = "Content-Security-Policy";
    public const string ContentSecurityPolicyValue = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https: blob:; font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'self';";

    public const string StrictTransportSecurityHeader = "Strict-Transport-Security";
    public const string StrictTransportSecurityValue = "max-age=31536000; includeSubDomains";

    private readonly RequestDelegate next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ApplySecurityHeaders(context);

        context.Response.OnStarting(() =>
        {
            ApplySecurityHeaders(context);
            return Task.CompletedTask;
        });

        await this.next(context);
    }

    private static void ApplySecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers[XFrameOptionsHeader] = XFrameOptionsValue;
        headers[XContentTypeOptionsHeader] = XContentTypeOptionsValue;
        headers[ReferrerPolicyHeader] = ReferrerPolicyValue;
        headers[PermissionsPolicyHeader] = PermissionsPolicyValue;
        headers[ContentSecurityPolicyHeader] = ContentSecurityPolicyValue;

        var isHttps = context.Request.IsHttps ||
                      string.Equals(context.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

        if (isHttps)
        {
            headers[StrictTransportSecurityHeader] = StrictTransportSecurityValue;
        }
    }
}
