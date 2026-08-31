// Copyright (c) PlaceholderCompany. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Leecharr.Http;

public class VersionedApiControllerAttribute : ApiControllerAttribute, IRouteTemplateProvider
{
    private readonly string resource;

    protected VersionedApiControllerAttribute(string resource, int version)
    {
        this.resource = resource;
        this.Version = version;
    }

    public int Version { get; }

    public string Template => $"api/v{this.Version}/{this.resource}";

    public int? Order => 0;

    public string Name { get; set; }
}

public class V1ApiControllerAttribute : VersionedApiControllerAttribute
{
    public V1ApiControllerAttribute(string resource)
        : base(resource, 1)
    {
    }
}
