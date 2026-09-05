// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace Leecharr.Api.V1.NzbVortex;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NzbVortexLoginConstraintAttribute : Attribute, IActionConstraint
{
    public int Order => 0;

    public bool Accept(ActionConstraintContext context)
    {
        var request = context.RouteContext.HttpContext.Request;

        // If the path starts with /nzbvortex, it is always handled by NzbVortexApiController
        if (request.Path.Value?.StartsWith("/nzbvortex", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        // For GET requests, AuthController does not handle GET, so accept
        if (HttpMethods.IsGet(request.Method))
        {
            return true;
        }

        // For POST /api/v1/auth/login:
        // If content-type is application/json, delegate to AuthController
        if (request.HasJsonContentType())
        {
            return false;
        }

        return true;
    }
}
