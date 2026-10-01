// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
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
        using var reader = new StreamReader(request.Body, encoding);
        var content = await reader.ReadToEndAsync();
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
