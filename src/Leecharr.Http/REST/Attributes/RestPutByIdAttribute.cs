// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Http.REST.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class RestPutByIdAttribute : HttpPutAttribute
{
    public RestPutByIdAttribute()
        : base("{id:int?}")
    {
    }
}
