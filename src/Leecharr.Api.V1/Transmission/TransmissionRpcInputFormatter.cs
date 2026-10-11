// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace Leecharr.Api.V1.Transmission;

public class TransmissionRpcInputFormatter : TextInputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public TransmissionRpcInputFormatter()
    {
        this.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        this.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded"));
        this.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/plain"));
        this.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("*/*"));
        this.SupportedEncodings.Add(Encoding.UTF8);
        this.SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanReadType(Type type)
    {
        return type == typeof(TransmissionRpcRequest);
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(encoding);

        var request = context.HttpContext.Request;
        request.EnableBuffering();

        string content = await ReadRawBodyAsync(request, encoding);

        if (request.HasFormContentType && (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('{')))
        {
            try
            {
                var form = await request.ReadFormAsync();
                if (form.TryGetValue("query", out var queryVal) && !string.IsNullOrWhiteSpace(queryVal))
                {
                    var qStr = queryVal.ToString();
                    if (qStr.TrimStart().StartsWith('{'))
                    {
                        content = qStr;
                    }
                }
                else if (form.TryGetValue("json", out var jsonVal) && !string.IsNullOrWhiteSpace(jsonVal))
                {
                    var jStr = jsonVal.ToString();
                    if (jStr.TrimStart().StartsWith('{'))
                    {
                        content = jStr;
                    }
                }

                if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('{'))
                {
                    foreach (var kv in form)
                    {
                        var val = kv.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(val) && val.TrimStart().StartsWith('{'))
                        {
                            content = val;
                            break;
                        }

                        var key = kv.Key;
                        if (!string.IsNullOrWhiteSpace(key) && key.TrimStart().StartsWith('{'))
                        {
                            content = key;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('{'))
                {
                    var parts = form.Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}");
                    var reconstructed = string.Join("&", parts);
                    if (!string.IsNullOrWhiteSpace(reconstructed) && reconstructed.TrimStart().StartsWith('{'))
                    {
                        content = reconstructed;
                    }
                }
            }
            catch
            {
                // Ignored
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = await ReadRawBodyAsync(request, encoding);
        }

        if (!string.IsNullOrWhiteSpace(content) && !content.TrimStart().StartsWith('{'))
        {
            var unescaped = Uri.UnescapeDataString(content).Trim();
            if (unescaped.StartsWith('{'))
            {
                content = unescaped;
            }
            else if (unescaped.StartsWith("query=", StringComparison.OrdinalIgnoreCase) && unescaped.Length > 6)
            {
                var candidate = unescaped[6..].Trim();
                if (candidate.StartsWith('{'))
                {
                    content = candidate;
                }
            }
            else if (unescaped.StartsWith("json=", StringComparison.OrdinalIgnoreCase) && unescaped.Length > 5)
            {
                var candidate = unescaped[5..].Trim();
                if (candidate.StartsWith('{'))
                {
                    content = candidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return await InputFormatterResult.SuccessAsync(new TransmissionRpcRequest());
        }

        try
        {
            var model = JsonSerializer.Deserialize<TransmissionRpcRequest>(content, JsonOptions);
            return await InputFormatterResult.SuccessAsync(model);
        }
        catch (Exception ex)
        {
            context.ModelState.AddModelError(context.ModelName, ex.Message);
            return await InputFormatterResult.FailureAsync();
        }
    }

    private static async Task<string> ReadRawBodyAsync(HttpRequest request, Encoding encoding)
    {
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return content;
    }
}
