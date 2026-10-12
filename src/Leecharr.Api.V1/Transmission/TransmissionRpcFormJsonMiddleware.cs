// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Leecharr.Api.V1.Transmission;

/// <summary>
/// Some Transmission clients POST JSON-RPC bodies with a form-urlencoded content type.
/// Unescaped ampersands in magnet URIs break form parsing and yield HTTP 400 before the RPC formatter runs.
/// </summary>
public class TransmissionRpcFormJsonMiddleware
{
    private readonly RequestDelegate next;

    public TransmissionRpcFormJsonMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (HttpMethods.IsPost(context.Request.Method) &&
            path.StartsWith("/transmission/rpc", StringComparison.OrdinalIgnoreCase) &&
            context.Request.ContentType?.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('{'))
            {
                context.Request.ContentType = "application/json";
            }
        }

        await this.next(context);
    }
}
