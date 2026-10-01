// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
        string content = null;

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, encoding, leaveOpen: true);
            content = await reader.ReadToEndAsync();
            request.Body.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(content) && request.HasFormContentType)
        {
            try
            {
                var form = await request.ReadFormAsync();
                var parts = form.Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}");
                var reconstructed = string.Join("&", parts);
                if (!string.IsNullOrWhiteSpace(reconstructed) && reconstructed.TrimStart().StartsWith('{'))
                {
                    content = reconstructed;
                }
            }
            catch
            {
                // Ignored
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            using var reader = new StreamReader(request.Body, encoding);
            content = await reader.ReadToEndAsync();
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
}
